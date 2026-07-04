using System.Net.Http.Headers;
using AdobeDownloader.Core.Models;
using AdobeDownloader.Core.Parsing;

namespace AdobeDownloader.Core;

/// <summary>Adobe API 调用失败异常。</summary>
public sealed class AdobeApiException : Exception
{
    public int? StatusCode { get; }
    public AdobeApiException(string message, int? statusCode = null, Exception? inner = null)
        : base(message, inner) => StatusCode = statusCode;
}

/// <summary>
/// 调用 Adobe 官方接口：获取产品目录、获取 application.json。
/// 对应原版 NewNetworkService。
/// </summary>
public sealed class AdobeApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiVersion;
    private readonly string? _authToken;

    public AdobeApiClient(HttpClient? http = null, string? apiVersion = null, string? authToken = null)
    {
        _http = http ?? new HttpClient();
        _apiVersion = string.IsNullOrEmpty(apiVersion) ? NetworkConstants.DefaultApiVersion : apiVersion;
        _authToken = authToken;
    }

    private static readonly string[] DefaultChannels = { "ccm", "sti", "nocc" };

    /// <summary>获取并解析产品目录（ccm 可见产品 + 全量依赖池）。</summary>
    public async Task<CatalogResult> FetchCatalogAsync(
        TargetArchitecture arch = TargetArchitecture.X64,
        CancellationToken ct = default)
        => CatalogParser.Parse(await FetchCatalogXmlAsync(arch, ct), visibleChannel: "ccm");

    /// <summary>仅获取产品目录的原始 XML（含依赖通道二次请求），供上层缓存到本地。</summary>
    public async Task<string> FetchCatalogXmlAsync(
        TargetArchitecture arch = TargetArchitecture.X64,
        CancellationToken ct = default)
    {
        var headers = NetworkConstants.FfcRequestHeaders(_apiVersion, _authToken);

        var primaryXml = await GetStringWithRetryAsync(
            BuildProductsUrl(arch, DefaultChannels), headers,
            NetworkConstants.FfcRequestTimeout, NetworkConstants.MaxRetryAttempts, ct);

        // 依赖产品（如 ACR/CCXP）常位于额外的 dependencyFFCChannel，需二次请求纳入
        var extraChannels = CatalogParser.ExtractDependencyChannels(primaryXml)
            .Except(DefaultChannels, StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToArray();

        if (extraChannels.Length == 0) return primaryXml;

        var allChannels = DefaultChannels.Concat(extraChannels).ToArray();
        return await GetStringWithRetryAsync(
            BuildProductsUrl(arch, allChannels), headers,
            NetworkConstants.FfcRequestTimeout, NetworkConstants.MaxRetryAttempts, ct);
    }

    /// <summary>获取指定构建的 application.json。</summary>
    public async Task<ApplicationInfo> FetchApplicationInfoAsync(
        string sapCode, string version, string platform, string buildGuid,
        CancellationToken ct = default)
    {
        var url = $"{NetworkConstants.ApplicationJsonUrlV3}" +
                  $"?name={Uri.EscapeDataString(sapCode)}" +
                  $"&version={Uri.EscapeDataString(version)}" +
                  $"&platform={Uri.EscapeDataString(platform)}";

        var headers = NetworkConstants.ApplicationJsonHeaders(_apiVersion);
        if (!string.IsNullOrEmpty(buildGuid))
            headers["x-adobe-build-guid"] = buildGuid;

        Exception? last = null;
        for (var attempt = 0; attempt < NetworkConstants.MaxServiceCallRetries; attempt++)
        {
            try
            {
                var json = await GetStringAsync(url, headers, NetworkConstants.ServiceCallTimeout,
                    allowStatus: code => (code >= 200 && code < 300) || code == 412, ct);

                if (json.Trim() == "Build is not operational")
                    throw new AdobeApiException(
                        $"该版本已被 Adobe 撤销 (SapCode: {sapCode}, version: {version})");
                if (string.IsNullOrWhiteSpace(json))
                    throw new AdobeApiException("收到空响应");

                return ApplicationParser.Parse(json);
            }
            catch (AdobeApiException ex) when (ex.StatusCode is null)
            {
                throw; // 撤销/空响应等业务错误不重试
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < NetworkConstants.MaxServiceCallRetries - 1)
                    await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)), ct);
            }
        }
        throw new AdobeApiException("获取 application.json 失败", inner: last);
    }

    private string BuildProductsUrl(TargetArchitecture arch, IEnumerable<string> channels)
    {
        var query = new List<string>();
        foreach (var c in channels)
            query.Add($"channel={Uri.EscapeDataString(c)}");
        foreach (var p in arch.CatalogPlatformIds())
            query.Add($"platform={Uri.EscapeDataString(p)}");
        query.Add("payload=true");
        query.Add("productType=Desktop");
        query.Add("_type=xml");
        return $"{NetworkConstants.ProductsUrl(_apiVersion)}?{string.Join("&", query)}";
    }

    private async Task<string> GetStringWithRetryAsync(
        string url, Dictionary<string, string> headers, TimeSpan timeout, int maxAttempts,
        CancellationToken ct)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await GetStringAsync(url, headers, timeout,
                    allowStatus: code => code >= 200 && code < 300, ct);
            }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < maxAttempts - 1)
                    await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct);
            }
        }
        var detail = last?.InnerException?.Message ?? last?.Message ?? "未知错误";
        throw new AdobeApiException($"网络请求失败：{detail}", inner: last);
    }

    private async Task<string> GetStringAsync(
        string url, Dictionary<string, string> headers, TimeSpan timeout,
        Func<int, bool> allowStatus, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (k, v) in headers)
        {
            if (k.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue; // GET 无正文，跳过 Content-Type
            req.Headers.TryAddWithoutValidation(k, v);
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        var code = (int)resp.StatusCode;
        if (!allowStatus(code))
        {
            var body = await SafeReadAsync(resp, cts.Token);
            throw new AdobeApiException($"HTTP {code}", code) { };
        }
        return await resp.Content.ReadAsStringAsync(cts.Token);
    }

    private static async Task<string?> SafeReadAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try { return await resp.Content.ReadAsStringAsync(ct); }
        catch { return null; }
    }
}
