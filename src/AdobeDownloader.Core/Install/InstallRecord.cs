namespace AdobeDownloader.Core.Install;

/// <summary>
/// 一次安装的记录：供写入 Windows 卸载(ARP)注册表项，以及我们自己的卸载器精确回删。
/// 只记录本引擎实际创建的东西（安装目录、快捷方式），卸载时只删这些，安全可控。
/// </summary>
public sealed class InstallRecord
{
    public string SapCode { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";

    /// <summary>主程序安装目录（[INSTALLDIR] 解析后），卸载时递归删除。</summary>
    public string InstallLocation { get; set; } = "";

    /// <summary>主程序 exe 全路径，作 DisplayIcon。</summary>
    public string MainExe { get; set; } = "";

    /// <summary>本次创建的快捷方式 .lnk 全路径，卸载时删除。</summary>
    public List<string> Shortcuts { get; set; } = new();

    /// <summary>ARP 注册表子键名（HKLM\...\Uninstall\ 下）。</summary>
    public string ArpKeyName { get; set; } = "";

    public DateTime InstalledAt { get; set; } = DateTime.Now;
}
