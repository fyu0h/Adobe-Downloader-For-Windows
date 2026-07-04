namespace AdobeDownloader.Core.Cleanup;

/// <summary>
/// 清理安全护栏（移植原版 CleanupProtectedResource 到 Windows）：
/// 只允许删除"确属 Adobe"的路径，绝不触碰系统目录、盘根或本工具自身。
/// 删除路径必须同时通过：非危险根、非受保护、且与 Adobe 相关。
/// </summary>
public static class CleanupSafety
{
    // 本工具关键字（防止把自己或下载目录删掉）。用无空格全称，避免误伤纯 "adobe"。
    private static readonly string[] ProtectedKeywords =
    {
        "adobedownloader",
    };

    private static readonly HashSet<string> DangerousPaths = BuildDangerousPaths();

    private static HashSet<string> BuildDangerousPaths()
    {
        string F(Environment.SpecialFolder f) => Environment.GetFolderPath(f);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p) { if (!string.IsNullOrEmpty(p)) set.Add(p!.TrimEnd('\\').ToLowerInvariant()); }

        Add(F(Environment.SpecialFolder.Windows));
        Add(F(Environment.SpecialFolder.System));
        Add(F(Environment.SpecialFolder.SystemX86));
        Add(F(Environment.SpecialFolder.ProgramFiles));
        Add(F(Environment.SpecialFolder.ProgramFilesX86));
        Add(F(Environment.SpecialFolder.CommonProgramFiles));
        Add(F(Environment.SpecialFolder.CommonProgramFilesX86));
        Add(F(Environment.SpecialFolder.CommonApplicationData));
        Add(F(Environment.SpecialFolder.ApplicationData));
        Add(F(Environment.SpecialFolder.LocalApplicationData));
        Add(F(Environment.SpecialFolder.UserProfile));
        Add(Path.GetTempPath());
        // C:\Users 根
        var users = Path.GetDirectoryName(F(Environment.SpecialFolder.UserProfile));
        Add(users);
        // Adobe 根目录（允许删子项，但不允许一次性删整个 Adobe 根，避免误伤）
        foreach (var pf in new[] { F(Environment.SpecialFolder.ProgramFiles), F(Environment.SpecialFolder.ProgramFilesX86),
                                   F(Environment.SpecialFolder.CommonProgramFiles), F(Environment.SpecialFolder.CommonProgramFilesX86),
                                   F(Environment.SpecialFolder.CommonApplicationData) })
            if (!string.IsNullOrEmpty(pf)) Add(Path.Combine(pf, "Adobe"));
        return set;
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd('\\'); }
        catch { return path.Trim().TrimEnd('\\'); }
    }

    /// <summary>盘根（如 C:\）或危险系统目录。</summary>
    public static bool IsDangerous(string path)
    {
        var n = Normalize(path);
        if (n.Length <= 3) return true;                     // 盘符根 C:\ / C:
        var lower = n.ToLowerInvariant();
        // 盘根形式 x:\ 或 x:
        if (lower.Length is 2 or 3 && lower[1] == ':') return true;
        return DangerousPaths.Contains(lower);
    }

    /// <summary>本工具自身或其下载目录等受保护路径。</summary>
    public static bool IsProtected(string path)
    {
        var lower = Normalize(path).ToLowerInvariant()
            .Replace(" ", "").Replace("-", "").Replace("_", "");
        return ProtectedKeywords.Any(k => lower.Contains(k));
    }

    /// <summary>看起来确属 Adobe（含 adobe / acrobat）。</summary>
    public static bool IsAdobeRelated(string path)
    {
        var lower = Normalize(path).ToLowerInvariant();
        return lower.Contains("adobe") || lower.Contains("acrobat");
    }

    /// <summary>综合判断某路径是否可安全删除。</summary>
    public static bool IsSafeToDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (IsProtected(path)) return false;
        if (IsDangerous(path)) return false;
        return IsAdobeRelated(path);
    }
}
