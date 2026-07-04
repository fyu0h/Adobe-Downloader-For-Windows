namespace AdobeDownloader.Core;

/// <summary>
/// Adobe 官方端点与请求头常量（Windows 化，对应原版 NetworkConstants）。
/// </summary>
public static class NetworkConstants
{
    public const string AdobeAppVersion = "6.8.1.856";
    public const string DefaultApiVersion = "6";

    public const int MaxRetryAttempts = 3;
    public const int MaxServiceCallRetries = 3;
    public const int BufferSize = 1024 * 1024;      // 1 MiB
    public const int MaxConcurrentDownloads = 3;

    public static readonly TimeSpan FfcRequestTimeout = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan ServiceCallTimeout = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(30);

    public static string ProductsUrl(string apiVersion)
        => $"https://prod-rel-ffc-ccm.oobesaas.adobe.com/adobe-ffc-external/core/v{apiVersion}/products/all";

    public const string ApplicationJsonUrlV3 = "https://cdn-ffc.oobesaas.adobe.com/core/v3/applications";

    /// <summary>Windows 版 User-Agent（原版为 Mac-x.y）。</summary>
    public static string UserAgent
    {
        get
        {
            var os = Environment.OSVersion.Version;
            return $"Creative Cloud/{AdobeAppVersion}/Win-{os.Major}.{os.Minor}";
        }
    }

    public static Dictionary<string, string> FfcRequestHeaders(string apiVersion, string? authToken = null)
    {
        var headers = new Dictionary<string, string>
        {
            ["x-adobe-app-id"] = "accc-hdcore-desktop",
            ["x-api-key"] = $"Creative Cloud_v{apiVersion}_4",
            ["User-Agent"] = UserAgent,
            ["x-adobe-app-version"] = AdobeAppVersion,
            ["Content-Type"] = "application/json",
        };
        if (!string.IsNullOrEmpty(authToken))
            headers["Authorization"] = $"Bearer {authToken}";
        return headers;
    }

    public static Dictionary<string, string> ApplicationJsonHeaders(string apiVersion) => new()
    {
        ["x-adobe-app-id"] = "accc-hdcore-desktop",
        ["x-api-key"] = $"Creative Cloud_v{apiVersion}_4",
        ["User-Agent"] = UserAgent,
    };

    public static Dictionary<string, string> DownloadHeaders(string apiVersion) => ApplicationJsonHeaders(apiVersion);
}
