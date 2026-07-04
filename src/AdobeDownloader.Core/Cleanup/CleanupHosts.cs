namespace AdobeDownloader.Core.Cleanup;

/// <summary>hosts 文件处理逻辑（纯逻辑，可测）。识别并移除屏蔽 Adobe 服务器的行。</summary>
public static class CleanupHosts
{
    public const string HostsPath = @"C:\Windows\System32\drivers\etc\hosts";

    /// <summary>是否为屏蔽 Adobe 的 hosts 行（非注释、含 adobe 域名）。</summary>
    public static bool IsAdobeHostsLine(string line)
    {
        var t = line.Trim();
        if (t.Length == 0 || t.StartsWith('#')) return false;
        var lower = t.ToLowerInvariant();
        return lower.Contains("adobe") || lower.Contains("practivate")
               || lower.Contains("wip.adobe") || lower.Contains("activate.adobe")
               || lower.Contains("lm.licenses.adobe") || lower.Contains("genuine.adobe");
    }

    /// <summary>返回移除 Adobe 行后的内容与移除数量。</summary>
    public static (string[] kept, int removed) RemoveAdobeLines(string[] lines)
    {
        var kept = lines.Where(l => !IsAdobeHostsLine(l)).ToArray();
        return (kept, lines.Length - kept.Length);
    }
}
