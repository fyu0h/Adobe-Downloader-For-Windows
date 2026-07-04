using System.IO;
using System.Text.Json;
using AdobeDownloader.Core.Install;
using Microsoft.Win32;

namespace AdobeDownloader.App.Install;

/// <summary>
/// 写/读/删 Windows 卸载(ARP)注册表项及对应的安装记录 JSON。
/// 使自装的 Adobe 产品出现在系统“应用”列表与本工具卸载列表，并可经我们自己的卸载器卸载。
/// 写 HKLM 需管理员权限（安装本就在提权进程中执行）。
/// </summary>
public static class InstallRegistry
{
    private const string UninstallRoot =
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>安装记录 JSON 目录（所有用户共享）。</summary>
    public static string RecordsDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "AdobeDownloader", "installed");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>写入安装记录 JSON 与 ARP 注册表项。selfExe = 本程序 exe，用作卸载命令。</summary>
    public static void Write(InstallRecord record, string selfExe)
    {
        Directory.CreateDirectory(RecordsDir);
        var recordPath = Path.Combine(RecordsDir, record.ArpKeyName + ".json");
        File.WriteAllText(recordPath, JsonSerializer.Serialize(record, JsonOptions));

        using var key = Registry.LocalMachine.CreateSubKey($@"{UninstallRoot}\{record.ArpKeyName}", writable: true);
        if (key is null) return;

        key.SetValue("DisplayName", record.DisplayName);
        if (!string.IsNullOrEmpty(record.Version)) key.SetValue("DisplayVersion", record.Version);
        key.SetValue("Publisher", "Adobe Inc.");
        if (!string.IsNullOrEmpty(record.MainExe)) key.SetValue("DisplayIcon", record.MainExe);
        if (!string.IsNullOrEmpty(record.InstallLocation)) key.SetValue("InstallLocation", record.InstallLocation);
        key.SetValue("UninstallString", $"\"{selfExe}\" --uninstall \"{recordPath}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        var sizeKb = TryGetSizeKb(record.InstallLocation);
        if (sizeKb > 0) key.SetValue("EstimatedSize", sizeKb, RegistryValueKind.DWord);
    }

    /// <summary>读取安装记录 JSON。</summary>
    public static InstallRecord? ReadRecord(string recordPath)
    {
        try
        {
            return File.Exists(recordPath)
                ? JsonSerializer.Deserialize<InstallRecord>(File.ReadAllText(recordPath))
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>删除 ARP 注册表项与安装记录 JSON。</summary>
    public static void Remove(InstallRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ArpKeyName)) return;

        try { Registry.LocalMachine.DeleteSubKeyTree($@"{UninstallRoot}\{record.ArpKeyName}", throwOnMissingSubKey: false); }
        catch { /* 键不存在或无权限，忽略 */ }

        try
        {
            var recordPath = Path.Combine(RecordsDir, record.ArpKeyName + ".json");
            if (File.Exists(recordPath)) File.Delete(recordPath);
        }
        catch { /* ignore */ }
    }

    private static readonly (Microsoft.Win32.RegistryKey Hive, string Root)[] UninstallLocations =
    {
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (Registry.CurrentUser,  @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    };

    /// <summary>
    /// 强制删除时：扫描三处卸载列表，删掉 DisplayName 或 InstallLocation 匹配的残留卸载项。
    /// 返回被删除的项名，供日志。
    /// </summary>
    public static List<string> RemoveArpEntriesFor(string installDir, string displayName)
    {
        var removed = new List<string>();
        var dir = installDir.TrimEnd('\\', '/');

        foreach (var (hive, root) in UninstallLocations)
        {
            try
            {
                using var rootKey = hive.OpenSubKey(root, writable: true);
                if (rootKey is null) continue;

                foreach (var name in rootKey.GetSubKeyNames())
                {
                    try
                    {
                        string? dn, loc;
                        using (var sub = rootKey.OpenSubKey(name))
                        {
                            if (sub is null) continue;
                            dn = sub.GetValue("DisplayName") as string;
                            loc = (sub.GetValue("InstallLocation") as string ?? "").TrimEnd('\\', '/');
                        }

                        var nameMatch = !string.IsNullOrEmpty(displayName)
                            && string.Equals(dn?.Trim(), displayName, StringComparison.OrdinalIgnoreCase);
                        var locMatch = !string.IsNullOrEmpty(loc)
                            && string.Equals(loc, dir, StringComparison.OrdinalIgnoreCase);

                        if (nameMatch || locMatch)
                        {
                            rootKey.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                            removed.Add(name);
                        }
                    }
                    catch { /* 跳过单个 */ }
                }
            }
            catch { /* 打不开根键 */ }
        }
        return removed;
    }

    private static long TryGetSizeKb(string dir)
    {
        try
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long bytes = 0;
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { bytes += new FileInfo(f).Length; } catch { /* 跳过单个文件 */ }
            }
            return bytes / 1024;
        }
        catch
        {
            return 0;
        }
    }
}
