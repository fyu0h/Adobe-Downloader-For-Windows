using System.Net;
using System.Net.Http.Headers;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core;

/// <summary>下载进度快照。</summary>
public sealed record DownloadProgress(
    long DownloadedBytes,
    long TotalBytes,
    int CompletedPackages,
    int TotalPackages,
    string CurrentPackage,
    double BytesPerSecond)
{
    public double Fraction => TotalBytes > 0 ? Math.Clamp((double)DownloadedBytes / TotalBytes, 0, 1) : 0;
}

/// <summary>
/// 并发下载一个 DownloadPlan 的所有包，支持断点续传、进度回报、大小校验，
/// 并生成 driver.xml。对应原版 NewDownloadUtils 的下载部分。
/// </summary>
public sealed class DownloadEngine
{
    private readonly HttpClient _http;
    private readonly string _cdn;
    private readonly string _apiVersion;

    public DownloadEngine(string cdn, HttpClient? http = null, string? apiVersion = null)
    {
        _cdn = cdn.TrimEnd('/');
        _http = http ?? new HttpClient();
        _apiVersion = string.IsNullOrEmpty(apiVersion) ? NetworkConstants.DefaultApiVersion : apiVersion;
    }

    /// <summary>
    /// 下载计划中的全部包到 <paramref name="destinationRoot"/>，并写入 driver.xml。
    /// </summary>
    public async Task DownloadAsync(
        DownloadPlan plan, string destinationRoot,
        IProgress<DownloadProgress>? progress = null,
        string installDir = DriverXmlGenerator.DefaultWindowsInstallDir,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationRoot);

        // driver.xml
        var driverXml = DriverXmlGenerator.Generate(plan, installDir);
        if (!string.IsNullOrEmpty(driverXml))
            await File.WriteAllTextAsync(Path.Combine(destinationRoot, "driver.xml"), driverXml, ct);

        var allPackages = plan.Components
            .SelectMany(c => c.Packages.Select(p => (component: c, package: p)))
            .ToList();

        var totalBytes = plan.TotalDownloadSize;
        long downloadedBytes = 0;
        var completed = 0;
        var totalPackages = allPackages.Count;
        var startTime = DateTime.UtcNow;
        var progressLock = new object();

        // 滑动窗口测速：用最近约 2 秒内下载的字节量算“当前速度”，
        // 避免累计平均把断点续传的已有字节或启动瞬时突发计入而虚高。
        var speedTracker = new SlidingWindowSpeed(windowSeconds: 2.0);

        void ReportProgress(string current)
        {
            long snapshotBytes;
            int snapshotCompleted;
            double speed;
            lock (progressLock)
            {
                snapshotBytes = downloadedBytes;
                snapshotCompleted = completed;
                var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                speed = speedTracker.Update(elapsed, snapshotBytes);
            }
            progress?.Report(new DownloadProgress(
                snapshotBytes, totalBytes, snapshotCompleted, totalPackages, current, speed));
        }

        using var throttle = new SemaphoreSlim(NetworkConstants.MaxConcurrentDownloads);

        var tasks = allPackages.Select(async item =>
        {
            await throttle.WaitAsync(ct);
            try
            {
                var (component, package) = item;
                var dir = Path.Combine(destinationRoot, component.SapCode);
                Directory.CreateDirectory(dir);
                var dest = Path.Combine(dir, package.FullPackageName);

                await DownloadPackageAsync(package, dest, bytesDelta =>
                {
                    lock (progressLock) downloadedBytes += bytesDelta;
                    ReportProgress(package.FullPackageName);
                }, ct);

                package.Downloaded = true;
                lock (progressLock) completed++;
                ReportProgress(package.FullPackageName);
            }
            finally
            {
                throttle.Release();
            }
        });

        await Task.WhenAll(tasks);
        ReportProgress("");
    }

    private async Task DownloadPackageAsync(
        DownloadPackage package, string destination, Action<long> onBytes, CancellationToken ct)
    {
        var url = NormalizeUrl(package.DownloadPath);
        if (string.IsNullOrEmpty(url))
            throw new AdobeApiException($"包 {package.FullPackageName} 缺少下载地址");

        // 断点续传：已存在部分文件则从其末尾继续
        long existing = 0;
        if (File.Exists(destination))
        {
            existing = new FileInfo(destination).Length;
            if (package.DownloadSize > 0 && existing == package.DownloadSize)
            {
                onBytes(existing);   // 已完整
                return;
            }
            if (package.DownloadSize > 0 && existing > package.DownloadSize)
            {
                File.Delete(destination); // 文件异常，重下
                existing = 0;
            }
        }
        if (existing > 0) onBytes(existing);

        for (var attempt = 0; attempt < NetworkConstants.MaxRetryAttempts; attempt++)
        {
            try
            {
                await DownloadRangeAsync(url, destination, existing, onBytes, ct);

                if (package.DownloadSize > 0)
                {
                    var actual = new FileInfo(destination).Length;
                    if (actual != package.DownloadSize)
                        throw new AdobeApiException(
                            $"{package.FullPackageName} 大小不一致，期望 {package.DownloadSize}，实际 {actual}");
                }
                return;
            }
            catch (Exception) when (attempt < NetworkConstants.MaxRetryAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(3 * (attempt + 1)), ct);
                existing = File.Exists(destination) ? new FileInfo(destination).Length : 0;
            }
        }
        // 最后一次失败向上抛
        await DownloadRangeAsync(url, destination, existing, onBytes, ct);
    }

    private async Task DownloadRangeAsync(
        string url, string destination, long resumeFrom, Action<long> onBytes, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        foreach (var (k, v) in NetworkConstants.DownloadHeaders(_apiVersion))
            req.Headers.TryAddWithoutValidation(k, v);
        if (resumeFrom > 0)
            req.Headers.Range = new RangeHeaderValue(resumeFrom, null);

        using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

        // 若服务器忽略 Range（返回 200 而非 206），从头写
        var append = resumeFrom > 0 && resp.StatusCode == HttpStatusCode.PartialContent;
        if (resumeFrom > 0 && resp.StatusCode == HttpStatusCode.OK)
        {
            append = false;
            resumeFrom = 0;
        }
        resp.EnsureSuccessStatusCode();

        var mode = append ? FileMode.Append : FileMode.Create;
        await using var fs = new FileStream(destination, mode, FileAccess.Write, FileShare.None,
            NetworkConstants.BufferSize, useAsync: true);
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);

        var buffer = new byte[NetworkConstants.BufferSize];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            await fs.WriteAsync(buffer.AsMemory(0, read), ct);
            onBytes(read);
        }
    }

    /// <summary>相对路径拼 CDN；已是绝对 URL 则原样返回（对应原版 normalizedPackageURL）。</summary>
    public string NormalizeUrl(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0) return "";
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var abs) &&
            (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
            return trimmed;
        var cleanPath = trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        return _cdn + cleanPath;
    }
}
