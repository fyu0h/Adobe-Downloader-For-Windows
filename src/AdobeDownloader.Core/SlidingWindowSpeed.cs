namespace AdobeDownloader.Core;

/// <summary>
/// 滑动窗口测速：根据最近一段时间窗口内累计字节的增量估算“当前下载速度”，
/// 而非从头到现在的累计平均——后者会把断点续传已有的字节或启动瞬时突发计入而虚高。
/// 非线程安全，调用方需自行加锁。
/// </summary>
public sealed class SlidingWindowSpeed
{
    private readonly double _windowSeconds;
    private readonly List<(double T, long Bytes)> _samples = new();

    public SlidingWindowSpeed(double windowSeconds = 2.0) => _windowSeconds = windowSeconds;

    /// <summary>
    /// 记录一个样本（<paramref name="elapsedSeconds"/> 为自开始经过的秒数，
    /// <paramref name="totalBytes"/> 为累计下载字节），返回窗口内的当前速度（字节/秒）。
    /// </summary>
    public double Update(double elapsedSeconds, long totalBytes)
    {
        _samples.Add((elapsedSeconds, totalBytes));

        var cutoff = elapsedSeconds - _windowSeconds;
        while (_samples.Count > 2 && _samples[0].T < cutoff)
            _samples.RemoveAt(0);

        var first = _samples[0];
        var last = _samples[^1];
        var span = last.T - first.T;
        return span > 0.001 ? (last.Bytes - first.Bytes) / span : 0;
    }
}
