namespace AdobeDownloader.Core.Cleanup;

/// <summary>
/// 清理类别（对应原版 CleanupOption，Windows 化）。每个类别定义一组要清理的目标：
/// 文件路径、通配符、注册表键、Windows 服务、hosts 行、凭据。
/// </summary>
public enum CleanupOption
{
    AdobeApps,
    AdobeCreativeCloud,
    AdobePreferences,
    AdobeCaches,
    AdobeLicenses,
    AdobeLogs,
    AdobeServices,
    AdobeKeychain,
    AdobeGenuineService,
    AdobeHosts,
}

public static class CleanupOptionExtensions
{
    public static string DisplayName(this CleanupOption o) => o switch
    {
        CleanupOption.AdobeApps => "Adobe 应用程序",
        CleanupOption.AdobeCreativeCloud => "Adobe Creative Cloud",
        CleanupOption.AdobePreferences => "Adobe 偏好设置",
        CleanupOption.AdobeCaches => "Adobe 缓存文件",
        CleanupOption.AdobeLicenses => "Adobe 许可文件",
        CleanupOption.AdobeLogs => "Adobe 日志文件",
        CleanupOption.AdobeServices => "Adobe 服务",
        CleanupOption.AdobeKeychain => "Adobe 钥匙串（凭据）",
        CleanupOption.AdobeGenuineService => "Adobe 正版验证服务",
        CleanupOption.AdobeHosts => "Adobe Hosts",
        _ => o.ToString(),
    };

    public static string Description(this CleanupOption o) => o switch
    {
        CleanupOption.AdobeApps => "已安装的 Adobe 应用程序（Photoshop、After Effects 等）",
        CleanupOption.AdobeCreativeCloud => "Adobe Creative Cloud 桌面端与桌面公共组件",
        CleanupOption.AdobePreferences => "用户偏好设置（AppData 与注册表 HKCU\\Software\\Adobe）",
        CleanupOption.AdobeCaches => "缓存与临时文件",
        CleanupOption.AdobeLicenses => "许可与激活文件（SLStore / SLCache / Adobe PCD）",
        CleanupOption.AdobeLogs => "安装与运行日志",
        CleanupOption.AdobeServices => "Adobe 后台 Windows 服务",
        CleanupOption.AdobeKeychain => "Windows 凭据管理器中的 Adobe 凭据",
        CleanupOption.AdobeGenuineService => "Adobe 正版验证服务（AGS）",
        CleanupOption.AdobeHosts => "hosts 文件中屏蔽 Adobe 服务器的条目",
        _ => "",
    };

    /// <summary>执行顺序：先停服务，再删文件，最后处理 hosts。</summary>
    public static readonly IReadOnlyList<CleanupOption> ExecutionOrder = new[]
    {
        CleanupOption.AdobeServices,
        CleanupOption.AdobeGenuineService,
        CleanupOption.AdobeApps,
        CleanupOption.AdobeCreativeCloud,
        CleanupOption.AdobePreferences,
        CleanupOption.AdobeCaches,
        CleanupOption.AdobeLicenses,
        CleanupOption.AdobeLogs,
        CleanupOption.AdobeKeychain,
        CleanupOption.AdobeHosts,
    };

    public static IReadOnlyList<CleanupTarget> Targets(this CleanupOption o) => o switch
    {
        CleanupOption.AdobeApps => new[]
        {
            CleanupTarget.Glob(o, @"%ProgramFiles%\Adobe\*", "Adobe 应用程序 (Program Files)"),
            CleanupTarget.Glob(o, @"%ProgramFiles(x86)%\Adobe\*", "Adobe 应用程序 (Program Files x86)"),
            CleanupTarget.Glob(o, @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Adobe*", "开始菜单 Adobe 快捷方式"),
            CleanupTarget.Glob(o, @"%ProgramData%\Microsoft\Windows\Start Menu\Programs\Adobe*.lnk", "开始菜单 Adobe 快捷方式"),
        },
        CleanupOption.AdobeCreativeCloud => new[]
        {
            CleanupTarget.Path(o, @"%ProgramFiles%\Adobe\Adobe Creative Cloud", "Creative Cloud 桌面端"),
            CleanupTarget.Path(o, @"%ProgramFiles%\Adobe\Adobe Creative Cloud Experience", "Creative Cloud Experience"),
            CleanupTarget.Path(o, @"%CommonProgramFiles(x86)%\Adobe\Adobe Desktop Common", "Adobe Desktop Common"),
            CleanupTarget.Path(o, @"%CommonProgramFiles%\Adobe\Adobe Desktop Common", "Adobe Desktop Common"),
            CleanupTarget.Path(o, @"%CommonProgramFiles(x86)%\Adobe\OOBE", "OOBE"),
            CleanupTarget.Path(o, @"%CommonProgramFiles%\Adobe\OOBE", "OOBE"),
            CleanupTarget.Path(o, @"%LOCALAPPDATA%\Adobe\OOBE", "OOBE 用户数据"),
        },
        CleanupOption.AdobePreferences => new[]
        {
            CleanupTarget.Path(o, @"%APPDATA%\Adobe", "Adobe 偏好设置 (Roaming)"),
            CleanupTarget.Registry(o, @"HKEY_CURRENT_USER\Software\Adobe", "偏好设置 (HKCU\\Software\\Adobe)"),
        },
        CleanupOption.AdobeCaches => new[]
        {
            CleanupTarget.Path(o, @"%LOCALAPPDATA%\Adobe", "Adobe 缓存 (Local)"),
            CleanupTarget.Glob(o, @"%TEMP%\Adobe*", "临时目录 Adobe 缓存"),
            CleanupTarget.Glob(o, @"%TEMP%\*Adobe*", "临时目录 Adobe 缓存"),
            CleanupTarget.Path(o, @"%ProgramData%\Adobe\Temp", "公共 Adobe 临时"),
        },
        CleanupOption.AdobeLicenses => new[]
        {
            CleanupTarget.Path(o, @"%ProgramData%\Adobe\SLStore", "许可 SLStore"),
            CleanupTarget.Path(o, @"%ProgramData%\Adobe\SLCache", "许可 SLCache"),
            CleanupTarget.Path(o, @"%CommonProgramFiles(x86)%\Adobe\Adobe PCD", "Adobe PCD"),
            CleanupTarget.Path(o, @"%CommonProgramFiles%\Adobe\Adobe PCD", "Adobe PCD"),
            CleanupTarget.Path(o, @"%LOCALAPPDATA%\Adobe\AdobeGCData", "AdobeGCData"),
        },
        CleanupOption.AdobeLogs => new[]
        {
            CleanupTarget.Glob(o, @"%CommonProgramFiles(x86)%\Adobe\Installers\*.log", "安装日志"),
            CleanupTarget.Glob(o, @"%CommonProgramFiles%\Adobe\Installers\*.log", "安装日志"),
            CleanupTarget.Glob(o, @"%LOCALAPPDATA%\Temp\*Adobe*.log", "运行日志"),
            CleanupTarget.Path(o, @"%LOCALAPPDATA%\Adobe\CameraRaw\Logs", "Camera Raw 日志"),
            CleanupTarget.Glob(o, @"%TEMP%\*.log", "临时日志（Adobe 相关）"),
        },
        CleanupOption.AdobeServices => new[]
        {
            CleanupTarget.Service(o, "AdobeARMservice", "Adobe Acrobat Update Service"),
            CleanupTarget.Service(o, "AdobeUpdateService", "Adobe Update Service"),
            CleanupTarget.Service(o, "Adobe Acrobat Update Service", "Adobe Acrobat Update Service"),
            CleanupTarget.Service(o, "AdobeFlashPlayerUpdateSvc", "Adobe Flash Player Update Service"),
        },
        CleanupOption.AdobeKeychain => new[]
        {
            CleanupTarget.Credential(o, "Adobe", "凭据管理器中的 Adobe 凭据"),
        },
        CleanupOption.AdobeGenuineService => new[]
        {
            CleanupTarget.Service(o, "AGSService", "Adobe Genuine Software Integrity Service"),
            CleanupTarget.Service(o, "AGMService", "Adobe Genuine Monitor Service"),
            CleanupTarget.Path(o, @"%CommonProgramFiles(x86)%\Adobe\AdobeGCClient", "AdobeGCClient"),
            CleanupTarget.Path(o, @"%CommonProgramFiles%\Adobe\AdobeGCClient", "AdobeGCClient"),
            CleanupTarget.Path(o, @"%ProgramData%\Adobe\AdobeGCClient", "AdobeGCClient 数据"),
        },
        CleanupOption.AdobeHosts => new[]
        {
            CleanupTarget.Hosts(o, "移除 hosts 中的 Adobe 屏蔽条目"),
        },
        _ => Array.Empty<CleanupTarget>(),
    };
}
