using System.Diagnostics;
using System.IO;
using AdobeDownloader.Core.Cleanup;
using Microsoft.Win32;

namespace AdobeDownloader.App.Cleanup;

/// <summary>
/// 扫描系统，为选中的清理类别生成实际存在的清理计划（对应原版 CleanupPlanner）。
/// 只生成通过安全护栏的项，供用户预览后再执行。
/// </summary>
public sealed class CleanupPlanner
{
    public CleanupPlan BuildPlan(IEnumerable<CleanupOption> options, IProgress<string>? progress = null)
    {
        var selected = options.ToHashSet();
        var ordered = CleanupOptionExtensions.ExecutionOrder.Where(selected.Contains);
        var items = new List<CleanupPlanItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var option in ordered)
        {
            progress?.Report($"正在分析 {option.DisplayName()} ...");
            foreach (var target in option.Targets())
            {
                foreach (var item in ResolveTarget(target))
                {
                    var key = $"{item.Kind}|{item.Target}";
                    if (seen.Add(key)) items.Add(item);
                }
            }
        }

        return new CleanupPlan { Items = items };
    }

    private IEnumerable<CleanupPlanItem> ResolveTarget(CleanupTarget target)
    {
        switch (target.Kind)
        {
            case CleanupActionKind.RemovePath:
                var path = Environment.ExpandEnvironmentVariables(target.Template);
                if ((Directory.Exists(path) || File.Exists(path)) && CleanupSafety.IsSafeToDelete(path))
                    yield return PathItem(target, path);
                break;

            case CleanupActionKind.RemoveGlob:
                foreach (var match in ExpandGlob(target.Template))
                    if (CleanupSafety.IsSafeToDelete(match))
                        yield return PathItem(target, match);
                break;

            case CleanupActionKind.RegistryKey:
                if (RegistryKeyExists(target.Template))
                    yield return new CleanupPlanItem
                    {
                        Option = target.Option, Kind = target.Kind,
                        Title = target.Description, Target = target.Template, EstimatedBytes = 0,
                    };
                break;

            case CleanupActionKind.Service:
                if (ServiceExists(target.Template))
                    yield return new CleanupPlanItem
                    {
                        Option = target.Option, Kind = target.Kind,
                        Title = $"{target.Description} ({target.Template})", Target = target.Template, EstimatedBytes = 0,
                    };
                break;

            case CleanupActionKind.HostsClean:
                if (HostsHasAdobeEntries())
                    yield return new CleanupPlanItem
                    {
                        Option = target.Option, Kind = target.Kind,
                        Title = target.Description, Target = HostsPath, EstimatedBytes = 0,
                    };
                break;

            case CleanupActionKind.Credential:
                foreach (var cred in ListAdobeCredentials(target.Template))
                    yield return new CleanupPlanItem
                    {
                        Option = target.Option, Kind = target.Kind,
                        Title = $"{target.Description}: {cred}", Target = cred, EstimatedBytes = 0,
                    };
                break;
        }
    }

    private static CleanupPlanItem PathItem(CleanupTarget target, string path) => new()
    {
        Option = target.Option,
        Kind = target.Kind,
        Title = target.Description,
        Target = path,
        EstimatedBytes = EstimateSize(path),
    };

    // ---- 展开与检查 ----

    /// <summary>简单 glob：仅支持最后一段含通配（如 …\Adobe* 或 …\*.log）。</summary>
    public static IEnumerable<string> ExpandGlob(string template)
    {
        var expanded = Environment.ExpandEnvironmentVariables(template);
        var idx = expanded.LastIndexOf('\\');
        if (idx < 0) yield break;
        var parent = expanded[..idx];
        var pattern = expanded[(idx + 1)..];

        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            if (Directory.Exists(expanded) || File.Exists(expanded)) yield return expanded;
            yield break;
        }
        if (!Directory.Exists(parent)) yield break;

        IEnumerable<string> entries;
        try { entries = Directory.EnumerateFileSystemEntries(parent, pattern, SearchOption.TopDirectoryOnly); }
        catch { yield break; }
        foreach (var e in entries) yield return e;
    }

    private static long EstimateSize(string path)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(f).Length; } catch { }
            }
            return total;
        }
        catch { return 0; }
    }

    public static bool RegistryKeyExists(string fullKey)
    {
        var (hive, sub) = SplitRegistry(fullKey);
        if (hive is null) return false;
        try { using var k = hive.OpenSubKey(sub); return k is not null; }
        catch { return false; }
    }

    private static (RegistryKey? hive, string sub) SplitRegistry(string fullKey)
    {
        var idx = fullKey.IndexOf('\\');
        var hiveName = idx < 0 ? fullKey : fullKey[..idx];
        var sub = idx < 0 ? "" : fullKey[(idx + 1)..];
        RegistryKey? hive = hiveName.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_USERS" => Registry.Users,
            _ => null,
        };
        return (hive, sub);
    }

    public static bool ServiceExists(string name)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", $"query \"{name}\"")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
            return p.ExitCode == 0; // 1060 = 服务不存在
        }
        catch { return false; }
    }

    public const string HostsPath = CleanupHosts.HostsPath;

    public static bool HostsHasAdobeEntries()
    {
        try
        {
            if (!File.Exists(HostsPath)) return false;
            return File.ReadLines(HostsPath).Any(CleanupHosts.IsAdobeHostsLine);
        }
        catch { return false; }
    }

    private static IEnumerable<string> ListAdobeCredentials(string filter)
    {
        var result = new List<string>();
        try
        {
            var psi = new ProcessStartInfo("cmdkey.exe", "/list")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (var line in output.Split('\n'))
            {
                var t = line.Trim();
                if (t.StartsWith("Target:", StringComparison.OrdinalIgnoreCase) &&
                    t.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    var name = t["Target:".Length..].Trim();
                    // 去掉可能的 "LegacyGeneric:target=" 前缀留完整目标名给 cmdkey /delete
                    result.Add(name);
                }
            }
        }
        catch { }
        return result;
    }
}
