namespace AdobeDownloader.Core;

/// <summary>
/// 定位 Adobe 官方 HyperDrive 安装器 Setup.exe（随 Creative Cloud 安装），
/// 用于读取 driver.xml 完成安装。Windows 版把"安装"委托给官方安装器，
/// 无需像 macOS 原版那样自行实现 HDPIM。
/// </summary>
public static class SetupLocator
{
    /// <summary>Setup.exe 的常见安装位置。</summary>
    public static IEnumerable<string> CandidatePaths()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var relatives = new[]
        {
            @"Common Files\Adobe\Adobe Desktop Common\HDBox\Setup.exe",
            @"Common Files\Adobe\Adobe Desktop Common\HD\Setup.exe",
            @"Common Files\Adobe\OOBE\PDApp\HD\Setup.exe",
        };

        foreach (var root in new[] { programFilesX86, programFiles })
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var rel in relatives)
                yield return Path.Combine(root, rel);
        }
    }

    /// <summary>返回第一个存在的 Setup.exe 路径，找不到返回 null。</summary>
    public static string? FindSetupExe()
        => CandidatePaths().FirstOrDefault(File.Exists);

    /// <summary>
    /// 构造调用 Setup.exe 安装的参数。需以管理员身份运行。
    /// 参数格式与 adobe-packager / CCMaker 一致。
    /// </summary>
    public static string BuildInstallArguments(string driverXmlPath)
        => $"--install=1 --driverXML=\"{driverXmlPath}\"";
}
