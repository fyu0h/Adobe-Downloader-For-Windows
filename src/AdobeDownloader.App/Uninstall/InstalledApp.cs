namespace AdobeDownloader.App.Uninstall;

/// <summary>
/// 一个已安装的 Adobe 程序（来自 Windows 卸载注册表项）。
/// 卸载时调用其自身注册的 UninstallString，交由厂商卸载器处理。
/// </summary>
public sealed class InstalledApp
{
    /// <summary>显示名，如 “Adobe Premiere Pro 2024”。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>版本号，如 “24.0.0”。</summary>
    public string Version { get; set; } = "";

    /// <summary>发行商，通常为 “Adobe Inc.”。</summary>
    public string Publisher { get; set; } = "";

    /// <summary>DisplayIcon 原始值，形如 "C:\...\app.exe,0"，供提取图标。</summary>
    public string IconSource { get; set; } = "";

    /// <summary>卸载命令（含参数），点击卸载时以管理员权限执行。</summary>
    public string UninstallString { get; set; } = "";

    /// <summary>静默卸载命令（若注册表提供），优先使用以减少交互。</summary>
    public string QuietUninstallString { get; set; } = "";

    /// <summary>安装目录（若有）。</summary>
    public string InstallLocation { get; set; } = "";

    /// <summary>估算占用大小（字节，来自 EstimatedSize KB×1024）。</summary>
    public long EstimatedSizeBytes { get; set; }

    /// <summary>是否具备可执行的卸载命令。</summary>
    public bool CanUninstall =>
        !string.IsNullOrWhiteSpace(QuietUninstallString) || !string.IsNullOrWhiteSpace(UninstallString);

    /// <summary>实际使用的卸载命令（优先静默）。</summary>
    public string EffectiveUninstallCommand =>
        !string.IsNullOrWhiteSpace(QuietUninstallString) ? QuietUninstallString : UninstallString;

    /// <summary>是否可强制删除（已知有效的 Adobe 安装目录）。</summary>
    public bool CanForceRemove =>
        !string.IsNullOrWhiteSpace(InstallLocation)
        && InstallLocation.Contains("Adobe", StringComparison.OrdinalIgnoreCase);
}
