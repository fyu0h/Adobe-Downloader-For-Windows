using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AdobeDownloader.App.ViewModels;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.App;

/// <summary>一个持久化的下载任务记录。</summary>
public sealed class PersistedTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DownloadPlan Plan { get; set; } = new();
    public string Directory { get; set; } = "";
    public TaskState State { get; set; } = TaskState.Paused;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 下载任务持久化：保存到 %AppData%\AdobeDownloader\tasks.json。
/// 任务一直保留（含已完成），直到用户手动删除。对应原版 TaskPersistenceManager。
/// </summary>
public sealed class TaskStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static string FilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdobeDownloader");
        System.IO.Directory.CreateDirectory(dir);
        return Path.Combine(dir, "tasks.json");
    }

    public List<PersistedTask> Load()
    {
        try
        {
            var path = FilePath();
            if (File.Exists(path))
                return JsonSerializer.Deserialize<List<PersistedTask>>(File.ReadAllText(path), Options)
                       ?? new List<PersistedTask>();
        }
        catch { /* 损坏则忽略 */ }
        return new List<PersistedTask>();
    }

    public void Save(IEnumerable<PersistedTask> tasks)
    {
        try
        {
            File.WriteAllText(FilePath(), JsonSerializer.Serialize(tasks.ToList(), Options));
        }
        catch { /* 忽略持久化失败 */ }
    }
}
