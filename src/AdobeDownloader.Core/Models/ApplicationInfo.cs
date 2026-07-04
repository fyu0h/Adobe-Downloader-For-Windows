namespace AdobeDownloader.Core.Models;

/// <summary>
/// application.json 解析结果（对应原版 ApplicationInfo）。
/// </summary>
public sealed class ApplicationInfo
{
    public string SapCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string CodexVersion { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public string BaseVersion { get; set; } = "";
    public string CompressionType { get; set; } = "";

    /// <summary>安装目录模板，如 [AdobeProgramFiles]\Adobe Bridge 2026（供 pimx 的 [INSTALLDIR] 展开）。</summary>
    public string InstallDir { get; set; } = "";
    public bool InstallDirFixed { get; set; }

    public List<string> SupportedLanguages { get; set; } = new();
    public List<string> SoftDependencies { get; set; } = new();
    public List<AppPackage> Packages { get; set; } = new();
    public List<AppModule> Modules { get; set; } = new();

    public string RawJson { get; set; } = "";
}

public sealed class AppPackage
{
    public string PackageName { get; set; } = "";
    public string FullPackageName { get; set; } = "";
    public string Type { get; set; } = "noncore";     // core / noncore
    public bool IsShared { get; set; }
    public string ProcessorFamily { get; set; } = "";  // 空=通用；否则如 x64/arm64
    public long DownloadSize { get; set; }
    public long ExtractSize { get; set; }
    public int InstallSequenceNumber { get; set; }
    public string Path { get; set; } = "";             // 相对 CDN 的下载路径
    public string PackageVersion { get; set; } = "";
    public string Condition { get; set; } = "";
    public string PackageHashKey { get; set; } = "";
    public string? ValidationUrlType1 { get; set; }
    public string? ValidationUrlType2 { get; set; }
    public HashSet<string> Features { get; set; } = new();
}

public sealed class AppModule
{
    public string Id { get; set; } = "";
    public List<string> ReferencePackages { get; set; } = new();
}
