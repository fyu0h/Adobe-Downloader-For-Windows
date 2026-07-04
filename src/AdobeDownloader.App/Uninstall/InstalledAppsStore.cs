using System.IO;
using System.Text.Json;

namespace AdobeDownloader.App.Uninstall;

/// <summary>
/// 已安装 Adobe 程序的扫描结果存储：内存 + 磁盘 JSON。
/// 软件启动时后台扫描并 Save，卸载窗口打开时直接读取（秒开）。
/// </summary>
public static class InstalledAppsStore
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdobeDownloader");

    private static readonly string FilePath = Path.Combine(Dir, "installed-apps.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>最近一次扫描结果（内存缓存）。</summary>
    public static IReadOnlyList<InstalledApp> Current { get; private set; } = new List<InstalledApp>();

    /// <summary>最近一次扫描完成时间。</summary>
    public static DateTime? ScannedAt { get; private set; }

    /// <summary>扫描完成时触发（供已打开的窗口刷新）。</summary>
    public static event Action? Updated;

    /// <summary>从磁盘读入上次扫描结果（供启动时立即展示旧数据）。</summary>
    public static void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var apps = JsonSerializer.Deserialize<List<InstalledApp>>(File.ReadAllText(FilePath));
            if (apps is not null)
            {
                Current = apps;
                ScannedAt = File.GetLastWriteTime(FilePath);
            }
        }
        catch { /* 缓存损坏则忽略 */ }
    }

    /// <summary>保存扫描结果到内存与磁盘，并通知订阅者。</summary>
    public static void Save(IReadOnlyList<InstalledApp> apps)
    {
        Current = apps;
        ScannedAt = DateTime.Now;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(apps, JsonOptions));
        }
        catch { /* 存盘失败不影响使用 */ }
        Updated?.Invoke();
    }
}
