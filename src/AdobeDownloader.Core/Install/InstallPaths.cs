namespace AdobeDownloader.Core.Install;

/// <summary>
/// 安装/卸载用到的路径判定：识别多产品共用的 Adobe 根目录、把安装目录收窄到单个产品目录、
/// 计算单文件资源的部署目标。纯函数，便于测试。
/// </summary>
public static class InstallPaths
{
    /// <summary>系统内置的 Adobe 共用根目录（Program Files / Common Files 下的 Adobe）。</summary>
    public static IReadOnlyList<string> DefaultSharedRoots { get; } = new[]
    {
        DriverXmlGenerator.DefaultWindowsInstallDir,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Adobe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Adobe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Adobe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFilesX86), "Adobe"),
    };

    private static string Normalize(string path) => path.Replace('/', '\\').TrimEnd('\\');

    /// <summary>
    /// 是否为多产品共用的 Adobe 根目录（删它会连带删掉其它产品）：
    /// 命中内置/额外根目录，或目录名本身就是 "Adobe"。
    /// </summary>
    public static bool IsSharedAdobeRoot(string dir, IEnumerable<string>? extraRoots = null)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        var d = Normalize(dir);
        if (string.Equals(Path.GetFileName(d), "Adobe", StringComparison.OrdinalIgnoreCase)) return true;
        return DefaultSharedRoots.Concat(extraRoots ?? Enumerable.Empty<string>())
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Any(r => string.Equals(Normalize(r), d, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 把安装目录收窄到单个产品目录。AEFT 等产品的 INSTALLDIR 就是 [AdobeProgramFiles] 本身，
    /// 真正的产品目录（如 Adobe After Effects 2026）是其子目录。
    /// 非共用根：原样返回；共用根：取第一个位于其子目录下的候选路径所在的那一级子目录；
    /// 无法收窄返回空串（调用方应放弃删除）。
    /// </summary>
    public static string NarrowToProductDir(
        string installDir, IEnumerable<string> candidates, IEnumerable<string>? extraRoots = null)
    {
        if (string.IsNullOrWhiteSpace(installDir)) return "";
        var root = Normalize(installDir);
        if (!IsSharedAdobeRoot(root, extraRoots)) return root;

        var prefix = root + "\\";
        foreach (var c in candidates)
        {
            if (string.IsNullOrWhiteSpace(c)) continue;
            var path = Normalize(c);
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var rest = path[prefix.Length..];
            var sep = rest.IndexOf('\\');
            if (sep <= 0) continue; // 直接位于根目录下的文件，无法据此确定产品子目录
            return prefix + rest[..sep];
        }
        return "";
    }

    /// <summary>
    /// 单文件资源的部署目标。pimx 的 target 通常是目录（与目录资源一致），
    /// 只有当 target 的文件名与源文件同名、或带同样扩展名时才视为目标文件路径。
    /// 例：source=...\Cinema 4D Installer.exe，target=...\CustomHook\win → ...\CustomHook\win\Cinema 4D Installer.exe。
    /// </summary>
    public static string FileAssetDestination(string source, string target)
    {
        var srcName = Path.GetFileName(Normalize(source));
        var t = Normalize(target);
        var targetName = Path.GetFileName(t);
        var srcExt = Path.GetExtension(srcName);

        var isFilePath =
            string.Equals(targetName, srcName, StringComparison.OrdinalIgnoreCase)
            || (srcExt.Length > 0 && string.Equals(Path.GetExtension(targetName), srcExt, StringComparison.OrdinalIgnoreCase));

        return isFilePath ? t : Path.Combine(t, srcName);
    }
}
