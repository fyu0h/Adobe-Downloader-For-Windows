namespace AdobeDownloader.Core.Install;

/// <summary>一个 pimx 包的安装清单（Windows）。对应原版 PIMXPackageInfo。</summary>
public sealed class PimxPackage
{
    public string PackageName { get; set; } = "";
    public string Type { get; set; } = "";              // core / noncore
    public string ProcessorFamily { get; set; } = "";

    public List<PimxAsset> Assets { get; set; } = new();
    public List<PimxRegistry> RegistryEntries { get; set; } = new();
    public List<PimxPermission> Permissions { get; set; } = new();
    public List<PimxShortcut> Shortcuts { get; set; } = new();
    public List<PimxFolderIcon> FolderIcons { get; set; } = new();
    public List<PimxRunProgram> RunPrograms { get; set; } = new();

    /// <summary>包级条件（如 [OSProcessorFamily]==64-bit）；空表示无条件。</summary>
    public string Condition { get; set; } = "";
}

/// <summary>文件/目录部署：把 Source 复制到 Target。</summary>
public sealed class PimxAsset
{
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public bool Recursive { get; set; }

    /// <summary>ignoreAsset="true"：不部署此资源（如 VC 运行库直接从 staging 运行，不复制）。</summary>
    public bool IgnoreAsset { get; set; }
}

/// <summary>运行外部程序（如 VC++ 运行库安装器 VC_redist.x64.exe /q /norestart）。</summary>
public sealed class PimxRunProgram
{
    public string Path { get; set; } = "";
    public List<string> Arguments { get; set; } = new();
    public bool IsThirdParty { get; set; }
}

/// <summary>注册表写入。Name="Default" 表示默认值。</summary>
public sealed class PimxRegistry
{
    public string Path { get; set; } = "";      // HKEY_...\子键
    public string Name { get; set; } = "";
    public string Type { get; set; } = "REG_SZ"; // REG_SZ / REG_BINARY / REG_DWORD
    public string Value { get; set; } = "";
}

/// <summary>注册表项权限（ACL）。</summary>
public sealed class PimxPermission
{
    public string Path { get; set; } = "";
    public string User { get; set; } = "";
    public string PermissionValue { get; set; } = "";
}

/// <summary>开始菜单快捷方式。</summary>
public sealed class PimxShortcut
{
    public string Target { get; set; } = "";
    public string Directory { get; set; } = "";
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
}

/// <summary>文件夹图标（desktop.ini）。</summary>
public sealed class PimxFolderIcon
{
    public string FolderPath { get; set; } = "";
    public string IconPath { get; set; } = "";
}
