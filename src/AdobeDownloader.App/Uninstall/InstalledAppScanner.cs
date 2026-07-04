using System.IO;
using Microsoft.Win32;

namespace AdobeDownloader.App.Uninstall;

/// <summary>
/// 扫描 Windows 卸载注册表项，找出已安装的 Adobe 程序。
/// 覆盖 64/32 位（HKLM 及 WOW6432Node）与当前用户（HKCU）三处卸载列表。
/// </summary>
public static class InstalledAppScanner
{
    private const string UninstallSubKey =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string UninstallSubKeyWow =
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>扫描并返回按名称排序、去重后的 Adobe 程序列表（注册表卸载项 + 磁盘安装目录）。</summary>
    public static List<InstalledApp> Scan()
    {
        var apps = new List<InstalledApp>();
        CollectFrom(Registry.LocalMachine, UninstallSubKey, apps);
        CollectFrom(Registry.LocalMachine, UninstallSubKeyWow, apps);
        CollectFrom(Registry.CurrentUser, UninstallSubKey, apps);

        // 同名（32/64 各注册一次）去重，保留信息更全的一条
        var merged = apps
            .GroupBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.CanUninstall)
                          .ThenByDescending(a => a.EstimatedSizeBytes).First())
            .ToList();

        MergeDiskProducts(merged);

        return merged
            .OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Adobe 常见产品安装根目录（自装/官方装都在此）
    private static readonly string[] ProductRoots =
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Adobe"),
    };

    // 非产品（Adobe 基础组件/服务），不列入卸载列表
    private static readonly string[] NonProductFolders =
    {
        "Adobe Desktop Common", "Adobe Creative Cloud", "Adobe Creative Cloud Experience",
        "Adobe Sync", "AdobeGCClient", "Adobe GC Client", "Adobe Genuine Service",
        "Adobe Application Manager", "AdobeApplicationManager", "Adobe Installers",
        "CameraRaw", "Adobe Common", "Temp",
    };

    /// <summary>
    /// 扫描磁盘上的 Adobe 产品目录：为注册表项补全安装目录（供强制删除），
    /// 并把注册表里没有、但磁盘上存在的产品作为“仅可强制删除”项补入。
    /// </summary>
    private static void MergeDiskProducts(List<InstalledApp> apps)
    {
        foreach (var root in ProductRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(root); }
            catch { continue; }

            foreach (var dir in subdirs)
            {
                var folderName = Path.GetFileName(dir);
                if (!LooksLikeProduct(folderName, dir)) continue;

                // 已有同名注册表项：补全其安装目录（很多 Adobe 项 InstallLocation 为空）
                var existing = apps.FirstOrDefault(
                    a => string.Equals(a.DisplayName, folderName, StringComparison.OrdinalIgnoreCase));
                if (existing is not null)
                {
                    if (string.IsNullOrWhiteSpace(existing.InstallLocation))
                        existing.InstallLocation = dir;
                    continue;
                }

                // 注册表没有：作为“仅可强制删除”项补入
                apps.Add(new InstalledApp
                {
                    DisplayName = folderName,
                    Publisher = "Adobe",
                    InstallLocation = dir,
                    IconSource = FindMainExe(dir),
                });
            }
        }
    }

    private static bool LooksLikeProduct(string folderName, string dir)
    {
        if (string.IsNullOrWhiteSpace(folderName)) return false;
        if (NonProductFolders.Any(n => folderName.Equals(n, StringComparison.OrdinalIgnoreCase))) return false;
        // 需像产品：名字以 Adobe 开头，且目录内含可执行文件
        if (!folderName.StartsWith("Adobe", StringComparison.OrdinalIgnoreCase)) return false;
        return EnumerateExes(dir).Any();
    }

    /// <summary>在产品目录里挑一个主 exe 作图标来源（取体积最大的主程序）。</summary>
    private static string FindMainExe(string dir)
    {
        var exes = EnumerateExes(dir).ToList();
        if (exes.Count == 0) return "";
        return exes.OrderByDescending(f => { try { return new FileInfo(f).Length; } catch { return 0L; } })
                   .First();
    }

    // 递归枚举 exe，忽略无权限的子目录，避免受限子目录导致整体失败
    private static IEnumerable<string> EnumerateExes(string dir)
    {
        try
        {
            return Directory.EnumerateFiles(dir, "*.exe",
                new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true });
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static void CollectFrom(RegistryKey hive, string subKeyPath, List<InstalledApp> into)
    {
        try
        {
            using var root = hive.OpenSubKey(subKeyPath);
            if (root is null) return;

            foreach (var name in root.GetSubKeyNames())
            {
                try
                {
                    using var key = root.OpenSubKey(name);
                    var app = TryReadApp(key);
                    if (app is not null) into.Add(app);
                }
                catch { /* 单项失败不影响整体 */ }
            }
        }
        catch { /* 打不开根键则忽略 */ }
    }

    private static InstalledApp? TryReadApp(RegistryKey? key)
    {
        if (key is null) return null;

        var displayName = key.GetValue("DisplayName") as string;
        if (string.IsNullOrWhiteSpace(displayName)) return null;

        // 排除系统组件、更新补丁与非 Adobe 项
        if (key.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (key.GetValue("ParentKeyName") is not null) return null; // 补丁/子项

        var publisher = key.GetValue("Publisher") as string ?? "";
        if (!IsAdobe(displayName, publisher)) return null;

        return new InstalledApp
        {
            DisplayName = displayName.Trim(),
            Version = (key.GetValue("DisplayVersion") as string ?? "").Trim(),
            Publisher = publisher.Trim(),
            IconSource = (key.GetValue("DisplayIcon") as string ?? "").Trim(),
            UninstallString = (key.GetValue("UninstallString") as string ?? "").Trim(),
            QuietUninstallString = (key.GetValue("QuietUninstallString") as string ?? "").Trim(),
            InstallLocation = (key.GetValue("InstallLocation") as string ?? "").Trim(),
            EstimatedSizeBytes = key.GetValue("EstimatedSize") is int kb ? (long)kb * 1024 : 0,
        };
    }

    private static bool IsAdobe(string displayName, string publisher)
        => publisher.Contains("Adobe", StringComparison.OrdinalIgnoreCase)
           || displayName.Contains("Adobe", StringComparison.OrdinalIgnoreCase);
}
