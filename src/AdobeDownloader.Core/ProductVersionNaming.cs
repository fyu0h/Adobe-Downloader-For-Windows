namespace AdobeDownloader.Core;

/// <summary>
/// 把 Adobe 技术版本号（如 Illustrator 30.6、Photoshop 27.0）换算成大众熟知的“年份版本”（2026）。
/// 各产品线的换算基数不同：年份 = 主版本号 + 偏移。偏移经真机已装产品的年份反向校验。
/// 目录里多数主力产品名不含年份，故用此表补足；未知产品则只显示版本号。
/// </summary>
public static class ProductVersionNaming
{
    // SapCode → 偏移：年份 = 主版本号 + 偏移
    private static readonly Dictionary<string, int> YearOffset = new(StringComparer.OrdinalIgnoreCase)
    {
        ["PHSP"] = 1999, // Photoshop        25→2024
        ["ILST"] = 1996, // Illustrator      28→2024, 30→2026
        ["AEFT"] = 2000, // After Effects    24→2024, 26→2026
        ["PPRO"] = 2000, // Premiere Pro     24→2024, 26→2026
        ["AME"]  = 2000, // Media Encoder    26→2026
        ["AUDT"] = 2000, // Audition         24→2024
        ["FLPR"] = 2000, // Animate          24→2024
        ["CHAR"] = 2000, // Character Animator 24→2024
        ["IDSN"] = 2005, // InDesign         19→2024
        ["AICY"] = 2005, // InCopy           19→2024
        ["KBRG"] = 2010, // Bridge           14→2024, 16→2026
        ["LTRM"] = 2011, // Lightroom Classic 13→2024
        ["DRWV"] = 2000, // Dreamweaver      21→2021
    };

    /// <summary>营销年份（如 2026）；无法换算时返回 null。</summary>
    public static int? Year(string sapCode, string version)
    {
        var (baseCode, _) = StripBeta(sapCode);
        if (!YearOffset.TryGetValue(baseCode, out var offset)) return null;

        var major = MajorVersion(version);
        if (major <= 0) return null;

        var year = major + offset;
        // 合理性护栏：只认 2015–2099，避免异常版本号算出离谱年份
        return year is >= 2015 and <= 2099 ? year : null;
    }

    /// <summary>
    /// 供 UI 显示的版本标签：能换算年份则为 “2026 (30.6)”，Beta 追加 “ Beta”；
    /// 不能换算则只显示原始版本号。
    /// </summary>
    public static string Label(string sapCode, string version)
    {
        var (_, isBeta) = StripBeta(sapCode);
        var year = Year(sapCode, version);
        var beta = isBeta ? " Beta" : "";
        return year is null ? $"{version}{beta}" : $"{year} ({version}){beta}";
    }

    /// <summary>取主版本号（首个点分整数）。"30.6"→30，"12.1.0"→12。</summary>
    private static int MajorVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return 0;
        var head = version.Split('.', 2)[0].Trim();
        return int.TryParse(head, out var n) ? n : 0;
    }

    /// <summary>去掉 SapCode 末尾的 BETA 后缀（如 PPROBETA→PPRO），返回是否为 Beta。</summary>
    private static (string baseCode, bool isBeta) StripBeta(string sapCode)
    {
        if (sapCode.EndsWith("BETA", StringComparison.OrdinalIgnoreCase))
            return (sapCode[..^4], true);
        return (sapCode, false);
    }
}
