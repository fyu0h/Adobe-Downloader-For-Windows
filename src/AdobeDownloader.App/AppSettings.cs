using System.IO;
using System.Text.Json;
using AdobeDownloader.Core;

namespace AdobeDownloader.App;

/// <summary>应用设置，持久化到 %AppData%\AdobeDownloader\settings.json。</summary>
public sealed class AppSettings
{
    public string DownloadDirectory { get; set; } = DefaultDownloadDir();
    public string DefaultLanguage { get; set; } = "ALL";
    public string Architecture { get; set; } = TargetArchitectureExtensions.Current.ToString();
    public string ApiVersion { get; set; } = NetworkConstants.DefaultApiVersion;
    public string InstallDirectory { get; set; } = DriverXmlGenerator.DefaultWindowsInstallDir;

    public TargetArchitecture GetArchitecture()
        => Enum.TryParse<TargetArchitecture>(Architecture, out var a) ? a : TargetArchitectureExtensions.Current;

    private static string ConfigPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdobeDownloader");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "settings.json");
    }

    private static string DefaultDownloadDir()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "AdobeDownloader");

    public static AppSettings Load()
    {
        try
        {
            var path = ConfigPath();
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
        }
        catch { /* 损坏则用默认值 */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(ConfigPath(),
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 忽略持久化失败 */ }
    }
}
