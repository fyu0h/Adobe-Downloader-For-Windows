using AdobeDownloader.Core;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class SlidingWindowSpeedTests
{
    private const long MB = 1024 * 1024;

    [Fact]
    public void SteadyDownload_ReportsRealRate()
    {
        var s = new SlidingWindowSpeed(windowSeconds: 2.0);
        // 每 0.5 秒下载 40 MB → 80 MB/s
        double speed = 0;
        long bytes = 0;
        for (var i = 1; i <= 10; i++)
        {
            bytes += 40 * MB;
            speed = s.Update(i * 0.5, bytes);
        }
        Assert.InRange(speed / MB, 78, 82); // ≈ 80 MB/s
    }

    [Fact]
    public void ResumedDownload_DoesNotCountPreexistingBytes()
    {
        // 断点续传：会话开始时已有 720 MB 在盘，随后按 80 MB/s 新下载。
        // 累计平均会算成 ~800 MB/s，滑动窗口应只反映新下载的 ~80 MB/s。
        var s = new SlidingWindowSpeed(windowSeconds: 2.0);
        s.Update(0.0, 720 * MB);              // t=0 已有 720MB
        s.Update(0.5, (long)(760 * MB));      // +40MB
        s.Update(1.0, (long)(800 * MB));      // +40MB
        var speed = s.Update(1.5, (long)(840 * MB)); // +40MB
        Assert.InRange(speed / MB, 78, 82);   // ≈ 80 MB/s，而非 ~560/800
    }

    [Fact]
    public void FirstSample_HasZeroSpeed()
    {
        var s = new SlidingWindowSpeed();
        Assert.Equal(0, s.Update(0.0, 100 * MB));
    }
}
