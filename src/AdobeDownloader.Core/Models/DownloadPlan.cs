namespace AdobeDownloader.Core.Models;

/// <summary>
/// 一次下载的完整计划：主产品 + 所有依赖，每个组件包含其包列表。
/// 对应原版 NewDownloadTask + DependenciesToDownload 的可下载信息。
/// </summary>
public sealed class DownloadPlan
{
    public string ProductId { get; set; } = "";        // 主产品 SapCode
    public string ProductVersion { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Language { get; set; } = "";          // 例如 zh_CN / ALL
    public string Platform { get; set; } = "win64";
    public TargetArchitecture Architecture { get; set; } = TargetArchitecture.X64;

    /// <summary>下载所用的 CDN 基址，随任务持久化，供重启后恢复下载。</summary>
    public string Cdn { get; set; } = "";

    public List<PlanComponent> Components { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public long TotalDownloadSize => Components.Sum(c => c.Packages.Sum(p => p.DownloadSize));
    [System.Text.Json.Serialization.JsonIgnore]
    public int TotalPackages => Components.Sum(c => c.Packages.Count);
}

/// <summary>一个组件（主产品或依赖）。EsdDirectory 用 SapCode。</summary>
public sealed class PlanComponent
{
    public string SapCode { get; set; } = "";
    public string Version { get; set; } = "";          // CodexVersion
    public string BaseVersion { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public string Platform { get; set; } = "win64";
    public string BuildGuid { get; set; } = "";
    public bool IsMainProduct { get; set; }

    public List<DownloadPackage> Packages { get; set; } = new();
}

/// <summary>一个待下载的包。</summary>
public sealed class DownloadPackage
{
    public string FullPackageName { get; set; } = "";
    public string Type { get; set; } = "noncore";
    public string DownloadPath { get; set; } = "";      // 相对 CDN 的 Path
    public long DownloadSize { get; set; }
    public string PackageVersion { get; set; } = "";

    // 运行期状态
    public long DownloadedBytes { get; set; }
    public bool Downloaded { get; set; }
}
