using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Principal;
using AdobeDownloader.Core;
using AdobeDownloader.Core.Install;
using Microsoft.Win32;

namespace AdobeDownloader.App.Install;

public sealed record InstallProgress(double Fraction, string Message);

/// <summary>
/// Windows 安装引擎：读取 driver.xml，逐包解压 pimx 清单并执行（部署文件、写注册表、
/// 设权限、建快捷方式、文件夹图标）。移植自原版 HDPIM 引擎（InstallManager + HDPIMCommandEngine），
/// 不依赖 Adobe 官方 Setup.exe。需以管理员身份运行。
/// </summary>
public sealed class WindowsInstaller
{
    private readonly AdobeApiClient _api;
    private readonly List<string> _log = new();

    // ARP 登记用：安装过程中收集本引擎创建的快捷方式与主产品安装目录
    private readonly List<CreatedShortcut> _createdShortcuts = new();
    private string _mainInstallDir = "";
    private string _mainSapCode = "";

    private sealed record CreatedShortcut(string LnkPath, string Name, string Target, string Directory);

    public IReadOnlyList<string> Log => _log;

    /// <summary>每产生一条日志时触发（供 UI 实时显示）。</summary>
    public event Action<string>? Logged;

    public WindowsInstaller(AdobeApiClient api) => _api = api;

    public async Task InstallAsync(
        string driverDir, IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var driverPath = Path.Combine(driverDir, "driver.xml");
        if (!File.Exists(driverPath))
            throw new FileNotFoundException("找不到 driver.xml", driverPath);

        var driver = DriverInfo.Parse(await File.ReadAllTextAsync(driverPath, ct));
        _mainSapCode = driver.Product.SapCode;
        var components = driver.AllComponentsInInstallOrder().ToList();

        for (var i = 0; i < components.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var comp = components[i];
            var baseFraction = (double)i / components.Count;
            progress?.Report(new InstallProgress(baseFraction, $"正在安装 {comp.SapCode} {comp.CodexVersion} ..."));
            await InstallComponentAsync(driverDir, comp, driver, progress, baseFraction, 1.0 / components.Count, ct);
        }

        // 登记到 Windows 卸载(ARP)注册表项，使其出现在系统“应用”与本工具卸载列表并可卸载
        WriteArpRecord(driver);

        progress?.Report(new InstallProgress(1.0, "安装完成"));
    }

    private async Task InstallComponentAsync(
        string driverDir, DriverComponent comp, DriverInfo driver,
        IProgress<InstallProgress>? progress, double baseFraction, double span, CancellationToken ct)
    {
        var esd = string.IsNullOrEmpty(comp.EsdDirectory) ? comp.SapCode : comp.EsdDirectory;
        var packageDir = Path.Combine(driverDir, esd);
        if (!Directory.Exists(packageDir))
        {
            Info($"跳过 {comp.SapCode}：找不到目录 {packageDir}");
            return;
        }

        var zips = Directory.GetFiles(packageDir, "*.zip");
        if (zips.Length == 0)
        {
            Info($"跳过 {comp.SapCode}：{packageDir} 下没有安装包");
            return;
        }

        // 解析 application.json 拿到 InstallDir 模板与压缩方式
        var (installDirTemplate, compressionType) = await ResolveAppMetaAsync(comp, ct);
        var decompress = Lzma2FileDecompressor.IsZipLzma2(compressionType);

        foreach (var zip in zips)
        {
            ct.ThrowIfCancellationRequested();
            var extractRoot = Path.Combine(packageDir, Path.GetFileNameWithoutExtension(zip));
            var pimx = FindPimx(extractRoot);
            if (pimx is null)
            {
                progress?.Report(new InstallProgress(baseFraction, $"正在解压 {Path.GetFileName(zip)} ..."));
                Directory.CreateDirectory(extractRoot);
                ZipFile.ExtractToDirectory(zip, extractRoot, overwriteFiles: true);
                pimx = FindPimx(extractRoot);
            }
            if (pimx is null)
            {
                Info($"跳过 {comp.SapCode}：解压后未找到 .pimx");
                continue;
            }

            var xml = PimxDecompressor.LoadXml(pimx);
            var pkg = PimxParser.Parse(xml, driver.InstallLanguage);

            var staging = ResolveStagingFolder(extractRoot);
            var props = InstallProperties.ForWindows(
                adobeProgramFiles: driver.InstallDir,
                installDirTemplate: installDirTemplate,
                stagingFolder: staging,
                installLanguage: driver.InstallLanguage,
                architecture: driver.TargetArchitecture);

            if (string.Equals(comp.SapCode, _mainSapCode, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrEmpty(_mainInstallDir))
                _mainInstallDir = props["INSTALLDIR"];

            Info($"[{comp.SapCode}] INSTALLDIR={props["INSTALLDIR"]} 解压={decompress}");
            ExecutePackage(pkg, props, decompress, progress, baseFraction, span, ct);
        }
    }

    private void ExecutePackage(
        PimxPackage pkg, InstallProperties props, bool decompress, IProgress<InstallProgress>? progress,
        double baseFraction, double span, CancellationToken ct)
    {
        // 1) 部署文件
        for (var i = 0; i < pkg.Assets.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var a = pkg.Assets[i];
            if (a.IgnoreAsset) continue; // ignoreAsset：不部署（如 VC 运行库从 staging 直接运行）
            var src = props.Resolve(a.Source);
            var dst = props.Resolve(a.Target);
            progress?.Report(new InstallProgress(
                baseFraction + span * 0.1 + span * 0.6 * i / Math.Max(1, pkg.Assets.Count),
                $"正在部署文件: {Path.GetFileName(dst)}"));
            DeployAsset(src, dst, decompress);
        }

        // 2) 注册表
        foreach (var r in pkg.RegistryEntries)
        {
            ct.ThrowIfCancellationRequested();
            TryDo($"写注册表 {r.Path}", () => WriteRegistry(r, props));
        }

        // 3) 权限（注册表键或文件系统路径）
        foreach (var p in pkg.Permissions)
            TryDo($"设权限 {p.Path}", () => ApplyPermission(p, props));

        // 4) 快捷方式
        foreach (var s in pkg.Shortcuts)
            TryDo($"创建快捷方式 {s.Name}", () => CreateShortcut(s, props));

        // 5) 文件夹图标
        foreach (var f in pkg.FolderIcons)
            TryDo("设置文件夹图标", () => SetFolderIcon(f, props));

        // 6) 运行外部程序（如 VC++ 运行库安装器），需在文件部署后；受包条件约束
        if (pkg.RunPrograms.Count > 0)
        {
            if (!props.EvaluateCondition(pkg.Condition))
                Info($"包条件不满足，跳过运行外部程序：{pkg.Condition}");
            else
                foreach (var rp in pkg.RunPrograms)
                    TryDo($"运行 {Path.GetFileName(rp.Path)}", () => RunProgram(rp, props, ct));
        }
    }

    // ---------- 运行外部程序（VC 运行库等） ----------

    private void RunProgram(PimxRunProgram rp, InstallProperties props, CancellationToken ct)
    {
        var exe = props.Resolve(rp.Path).Replace('/', '\\');
        if (!File.Exists(exe))
        {
            Info($"[警告] 外部程序不存在，跳过：{exe}");
            return;
        }

        var args = string.Join(" ", rp.Arguments.Select(a => props.Resolve(a)));
        Info($"运行外部程序：{Path.GetFileName(exe)} {args}");

        using var proc = Process.Start(new ProcessStartInfo(exe, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
        }) ?? throw new InvalidOperationException($"无法启动 {exe}");

        while (!proc.WaitForExit(500))
            ct.ThrowIfCancellationRequested();

        var code = proc.ExitCode;
        // 常见成功码：0 成功；3010/1641 需重启；1638 已安装更新版本
        if (code is 0 or 3010 or 1641 or 1638)
            Info($"外部程序完成（退出码 {code}）");
        else
            Info($"[警告] 外部程序退出码 {code}，可能未成功安装");
    }

    // ---------- 文件部署 ----------

    private void DeployAsset(string source, string target, bool decompress)
    {
        if (Directory.Exists(source))
            CopyDirectory(source, target, decompress);
        else if (File.Exists(source))
            DeployFile(source, target, decompress);
        else
            Info($"资源不存在，跳过: {source}");
    }

    private static void CopyDirectory(string sourceDir, string destDir, bool decompress)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir))
            DeployFile(file, Path.Combine(destDir, Path.GetFileName(file)), decompress);
        foreach (var dir in Directory.EnumerateDirectories(sourceDir))
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)), decompress);
    }

    /// <summary>
    /// 部署单个文件：Zip-Lzma2 包内文件本身是 LZMA2 压缩的，需解压；.pimx 与非压缩包直接复制。
    /// </summary>
    private static void DeployFile(string source, string target, bool decompress)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (decompress && !source.EndsWith(".pimx", StringComparison.OrdinalIgnoreCase))
            Lzma2FileDecompressor.DecompressToFile(source, target);
        else
            File.Copy(source, target, overwrite: true);
    }

    // ---------- 注册表 ----------

    private static void WriteRegistry(PimxRegistry r, InstallProperties props)
    {
        var (hive, subKey) = SplitRegistryPath(r.Path);
        using var key = hive.CreateSubKey(subKey, writable: true);
        if (key is null) return;

        var name = string.Equals(r.Name, "Default", StringComparison.OrdinalIgnoreCase) ? "" : r.Name;
        var value = props.Resolve(r.Value);

        switch (r.Type.ToUpperInvariant())
        {
            case "REG_BINARY":
                key.SetValue(name, HexToBytes(value), RegistryValueKind.Binary);
                break;
            case "REG_DWORD":
                key.SetValue(name, int.TryParse(value, out var dw) ? dw : 0, RegistryValueKind.DWord);
                break;
            default: // REG_SZ / REG_EXPAND_SZ
                key.SetValue(name, value, RegistryValueKind.String);
                break;
        }
    }

    /// <summary>Permission 命令可作用于注册表键（HKEY_ 开头）或文件系统路径，分别处理。</summary>
    private static void ApplyPermission(PimxPermission p, InstallProperties props)
    {
        var path = props.Resolve(p.Path);
        var account = string.Equals(p.User, "Everyone", StringComparison.OrdinalIgnoreCase)
            ? (IdentityReference)new SecurityIdentifier(WellKnownSidType.WorldSid, null)
            : new NTAccount(p.User);

        if (path.StartsWith("HKEY_", StringComparison.OrdinalIgnoreCase))
            ApplyRegistryPermission(path, p.PermissionValue, account);
        else
            ApplyFileSystemPermission(path, p.PermissionValue, account);
    }

    private static void ApplyRegistryPermission(string path, string permissionValue, IdentityReference account)
    {
        var (hive, subKey) = SplitRegistryPath(path);
        using var key = hive.CreateSubKey(subKey, writable: true);
        if (key is null) return;

        var rights = permissionValue.ToUpperInvariant() switch
        {
            "GENERIC_ALL" or "FULLCONTROL" => RegistryRights.FullControl,
            "GENERIC_WRITE" => RegistryRights.WriteKey,
            _ => RegistryRights.ReadKey,
        };
        var security = key.GetAccessControl();
        security.AddAccessRule(new RegistryAccessRule(
            account, rights, InheritanceFlags.ContainerInherit, PropagationFlags.None, AccessControlType.Allow));
        key.SetAccessControl(security);
    }

    private static void ApplyFileSystemPermission(string path, string permissionValue, IdentityReference account)
    {
        Directory.CreateDirectory(path); // Adobe 常给尚不存在的目录预设权限
        var rights = permissionValue.ToUpperInvariant() switch
        {
            "GENERIC_ALL" or "FULLCONTROL" => FileSystemRights.FullControl,
            "GENERIC_WRITE" => FileSystemRights.Modify,
            _ => FileSystemRights.ReadAndExecute,
        };
        var info = new DirectoryInfo(path);
        var security = info.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            account, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        info.SetAccessControl(security);
    }

    private static (RegistryKey hive, string subKey) SplitRegistryPath(string path)
    {
        var idx = path.IndexOf('\\');
        var hiveName = idx < 0 ? path : path[..idx];
        var sub = idx < 0 ? "" : path[(idx + 1)..];
        RegistryKey hive = hiveName.ToUpperInvariant() switch
        {
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_USERS" => Registry.Users,
            "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => Registry.LocalMachine,
        };
        return (hive, sub);
    }

    private static byte[] HexToBytes(string hex)
    {
        var clean = new string(hex.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length % 2 != 0) clean = clean[..^1];
        var bytes = new byte[clean.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = byte.Parse(clean.AsSpan(i * 2, 2), NumberStyles.HexNumber);
        return bytes;
    }

    // ---------- 快捷方式 ----------

    private void CreateShortcut(PimxShortcut s, InstallProperties props)
    {
        var dir = props.Resolve(s.Directory);
        var name = props.Resolve(s.Name);
        var target = props.Resolve(s.Target);
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target))
            return;

        // 含未解析变量（如未定义的 [Xxx]）时中止，避免在工作目录建出 "[StartMenu]" 之类垃圾目录
        if (dir.Contains('[') || target.Contains('['))
            throw new InvalidOperationException($"快捷方式含未解析变量：目录={dir} 目标={target}");

        Directory.CreateDirectory(dir);
        var lnkPath = Path.Combine(dir, name + ".lnk");

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建 WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic lnk = shell.CreateShortcut(lnkPath);
        lnk.TargetPath = target;
        if (!string.IsNullOrEmpty(s.Arguments)) lnk.Arguments = props.Resolve(s.Arguments);
        lnk.WorkingDirectory = string.IsNullOrEmpty(s.WorkingDirectory)
            ? Path.GetDirectoryName(target) ?? ""
            : props.Resolve(s.WorkingDirectory);
        if (!string.IsNullOrEmpty(s.IconPath)) lnk.IconLocation = props.Resolve(s.IconPath) + ",0";
        lnk.Save();

        _createdShortcuts.Add(new CreatedShortcut(lnkPath, name, target, dir));
    }

    // ---------- 卸载(ARP)登记 ----------

    /// <summary>安装完成后写 Windows 卸载注册表项 + 安装记录，供系统“应用”与本工具卸载。</summary>
    private void WriteArpRecord(DriverInfo driver)
    {
        try
        {
            if (string.IsNullOrEmpty(_mainInstallDir) || !Directory.Exists(_mainInstallDir))
            {
                Info("跳过系统卸载登记：未能确定安装目录");
                return;
            }

            // 优先取开始菜单里的快捷方式作展示名与主 exe
            var startMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var chosen = _createdShortcuts.FirstOrDefault(
                             s => s.Directory.StartsWith(startMenu, StringComparison.OrdinalIgnoreCase))
                         ?? _createdShortcuts.FirstOrDefault();

            var displayName = chosen?.Name ?? Path.GetFileName(_mainInstallDir.TrimEnd('\\'));
            var mainExe = chosen?.Target ?? "";
            if (string.IsNullOrEmpty(mainExe) || !File.Exists(mainExe))
                mainExe = Directory.EnumerateFiles(_mainInstallDir, "*.exe", SearchOption.AllDirectories)
                              .FirstOrDefault() ?? "";

            var record = new InstallRecord
            {
                SapCode = driver.Product.SapCode,
                DisplayName = displayName,
                Version = driver.Product.CodexVersion,
                InstallLocation = _mainInstallDir,
                MainExe = mainExe,
                Shortcuts = _createdShortcuts.Select(s => s.LnkPath).ToList(),
                ArpKeyName = $"AdobeDownloader.{driver.Product.SapCode}.{driver.Product.CodexVersion}",
            };

            var selfExe = Environment.ProcessPath ?? "";
            InstallRegistry.Write(record, selfExe);
            Info($"已登记到系统卸载列表：{displayName}（{record.ArpKeyName}）");
        }
        catch (Exception ex)
        {
            Info($"[警告] 登记系统卸载项失败：{ex.Message}");
        }
    }

    // ---------- 文件夹图标 ----------

    private static void SetFolderIcon(PimxFolderIcon f, InstallProperties props)
    {
        var folder = props.Resolve(f.FolderPath);
        var icon = props.Resolve(f.IconPath);
        if (!Directory.Exists(folder)) return;

        var iniPath = Path.Combine(folder, "desktop.ini");
        if (File.Exists(iniPath))
            File.SetAttributes(iniPath, FileAttributes.Normal); // 清除隐藏/系统属性以便覆盖
        File.WriteAllText(iniPath,
            "[.ShellClassInfo]\r\n" +
            $"IconResource={icon},0\r\n" +
            $"IconFile={icon}\r\nIconIndex=0\r\n");
        File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);
        var attrs = File.GetAttributes(folder);
        File.SetAttributes(folder, attrs | FileAttributes.ReadOnly);
    }

    // ---------- 辅助 ----------

    private async Task<(string installDir, string compressionType)> ResolveAppMetaAsync(
        DriverComponent comp, CancellationToken ct)
    {
        try
        {
            var app = await _api.FetchApplicationInfoAsync(
                comp.SapCode, comp.CodexVersion, comp.Platform, comp.BuildGuid, ct);
            var dir = string.IsNullOrEmpty(app.InstallDir)
                ? $"[AdobeProgramFiles]\\{comp.SapCode}"
                : app.InstallDir;
            return (dir, app.CompressionType);
        }
        catch (Exception ex)
        {
            Info($"获取 {comp.SapCode} 的 application.json 失败：{ex.Message}");
        }
        // 回退：装到 [AdobeProgramFiles]\<SapCode>，压缩方式未知
        return ($"[AdobeProgramFiles]\\{comp.SapCode}", "");
    }

    private static string? FindPimx(string extractRoot)
        => Directory.Exists(extractRoot)
            ? Directory.GetFiles(extractRoot, "*.pimx", SearchOption.TopDirectoryOnly).FirstOrDefault()
            : null;

    private static string ResolveStagingFolder(string extractRoot)
    {
        // 包内容位于数字 media 目录（通常 "1"）下
        var numeric = Directory.EnumerateDirectories(extractRoot)
            .FirstOrDefault(d => int.TryParse(Path.GetFileName(d), out _));
        return numeric ?? extractRoot;
    }

    private void TryDo(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Info($"[警告] {what} 失败：{ex.Message}"); }
    }

    private void Info(string message)
    {
        _log.Add(message);
        Logged?.Invoke(message);
    }
}
