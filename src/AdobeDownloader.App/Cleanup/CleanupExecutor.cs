using System.Diagnostics;
using System.IO;
using AdobeDownloader.Core.Cleanup;
using Microsoft.Win32;

namespace AdobeDownloader.App.Cleanup;

public sealed record CleanupProgress(double Fraction, string Message);

/// <summary>
/// 执行清理计划（对应原版通过特权 Helper 执行 rm/launchctl/security 等）。
/// 删除前对文件路径再次做安全校验；逐项容错；需以管理员运行。
/// </summary>
public sealed class CleanupExecutor
{
    private readonly List<string> _log = new();
    public IReadOnlyList<string> Log => _log;
    public long RemovedBytes { get; private set; }
    public int RemovedCount { get; private set; }

    public async Task ExecuteAsync(CleanupPlan plan, IProgress<CleanupProgress>? progress = null, CancellationToken ct = default)
    {
        var items = plan.Items;
        for (var i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = items[i];
            progress?.Report(new CleanupProgress((double)i / Math.Max(1, items.Count), $"清理: {item.Title}"));
            await Task.Run(() => ExecuteItem(item), ct);
        }
        progress?.Report(new CleanupProgress(1.0, "清理完成"));
    }

    private void ExecuteItem(CleanupPlanItem item)
    {
        try
        {
            switch (item.Kind)
            {
                case CleanupActionKind.RemovePath:
                case CleanupActionKind.RemoveGlob:
                    if (!CleanupSafety.IsSafeToDelete(item.Target))
                    {
                        _log.Add($"[跳过-安全] {item.Target}");
                        return;
                    }
                    ForceDelete(item.Target);
                    RemovedBytes += item.EstimatedBytes;
                    RemovedCount++;
                    _log.Add($"[删除] {item.Target}");
                    break;

                case CleanupActionKind.RegistryKey:
                    DeleteRegistryKey(item.Target);
                    RemovedCount++;
                    _log.Add($"[注册表] {item.Target}");
                    break;

                case CleanupActionKind.Service:
                    StopAndDeleteService(item.Target);
                    RemovedCount++;
                    _log.Add($"[服务] 已停止并删除 {item.Target}");
                    break;

                case CleanupActionKind.HostsClean:
                    var removed = CleanHosts();
                    _log.Add($"[hosts] 移除 {removed} 行 Adobe 条目");
                    break;

                case CleanupActionKind.Credential:
                    DeleteCredential(item.Target);
                    RemovedCount++;
                    _log.Add($"[凭据] 已删除 {item.Target}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Add($"[失败] {item.Title}: {ex.Message}");
        }
    }

    // ---- 文件 ----

    private static void ForceDelete(string path)
    {
        if (File.Exists(path))
        {
            ClearReadonly(path);
            File.Delete(path);
            return;
        }
        if (!Directory.Exists(path)) return;

        // 递归清除只读属性后删除
        foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            ClearReadonly(f);
        try { new DirectoryInfo(path).Attributes = FileAttributes.Directory; } catch { }
        Directory.Delete(path, recursive: true);
    }

    private static void ClearReadonly(string file)
    {
        try
        {
            var attrs = File.GetAttributes(file);
            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                File.SetAttributes(file, FileAttributes.Normal);
        }
        catch { }
    }

    // ---- 注册表 ----

    private static void DeleteRegistryKey(string fullKey)
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
        if (hive is null || sub.Length == 0) return;
        hive.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
    }

    // ---- 服务 ----

    private static void StopAndDeleteService(string name)
    {
        RunSc($"stop \"{name}\"");
        RunSc($"delete \"{name}\"");
    }

    private static void RunSc(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("sc.exe", args)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
        }
        catch { }
    }

    // ---- hosts ----

    private static int CleanHosts()
    {
        var path = CleanupHosts.HostsPath;
        if (!File.Exists(path)) return 0;
        var (kept, removed) = CleanupHosts.RemoveAdobeLines(File.ReadAllLines(path));
        if (removed > 0)
        {
            ClearReadonly(path);
            File.WriteAllLines(path, kept);
        }
        return removed;
    }

    // ---- 凭据 ----

    private static void DeleteCredential(string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmdkey.exe", $"/delete:{target}")
            {
                UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(5000);
        }
        catch { }
    }
}
