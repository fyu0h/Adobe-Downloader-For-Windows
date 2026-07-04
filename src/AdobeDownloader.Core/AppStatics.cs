namespace AdobeDownloader.Core;

/// <summary>静态数据，移植自原版 AppStatics。</summary>
public static class AppStatics
{
    /// <summary>Adobe 支持的安装语言（code, 名称）。</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> SupportedLanguages = new[]
    {
        ("ALL", "ALL（全部语言）"),
        ("en_US", "English (US)"),
        ("zh_CN", "简体中文"),
        ("zh_TW", "繁體中文"),
        ("ja_JP", "日本語"),
        ("ko_KR", "한국어"),
        ("fr_FR", "Français"),
        ("fr_CA", "Français (Canada)"),
        ("de_DE", "Deutsch"),
        ("es_ES", "Español"),
        ("es_MX", "Español (Mexico)"),
        ("it_IT", "Italiano"),
        ("pt_BR", "Português (Brasil)"),
        ("ru_RU", "Русский"),
        ("nl_NL", "Nederlands"),
        ("pl_PL", "Polski"),
        ("da_DK", "Dansk"),
        ("sv_SE", "Svenska"),
        ("nb_NO", "Norsk"),
        ("fi_FI", "Suomi"),
        ("tr_TR", "Türkçe"),
        ("uk_UA", "Українська"),
        ("cs_CZ", "Čeština"),
        ("hu_HU", "Magyar"),
        ("en_GB", "English (UK)"),
        ("en_IL", "English (Israel)"),
        ("en_AE", "English (UAE)"),
        ("fr_MA", "Français (Maroc)"),
    };
}
