using System.Runtime.InteropServices;

namespace AdobeDownloader.Core;

/// <summary>
/// Windows 目标架构。Adobe 平台标识：win64（x64）、winarm64（ARM64）。
/// 对应原版 macOS 的 macarm64 / macuniversal / osx10-64。
/// </summary>
public enum TargetArchitecture
{
    X64,
    Arm64,
}

public static class TargetArchitectureExtensions
{
    /// <summary>当前运行主机的架构。</summary>
    public static TargetArchitecture Current =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? TargetArchitecture.Arm64
            : TargetArchitecture.X64;

    /// <summary>Adobe 平台标识符。</summary>
    public static string PlatformId(this TargetArchitecture arch) => arch switch
    {
        TargetArchitecture.Arm64 => "winarm64",
        _ => "win64",
    };

    /// <summary>driver.xml 中的 TargetArchitecture 值。</summary>
    public static string DriverValue(this TargetArchitecture arch) => arch switch
    {
        TargetArchitecture.Arm64 => "arm64",
        _ => "x64",
    };

    /// <summary>请求目录时使用的平台列表（优先当前架构，同时带上另一个作兜底）。</summary>
    public static IReadOnlyList<string> CatalogPlatformIds(this TargetArchitecture arch) => arch switch
    {
        TargetArchitecture.Arm64 => new[] { "winarm64", "win64" },
        _ => new[] { "win64" },
    };

    public static string DisplayName(this TargetArchitecture arch) => arch switch
    {
        TargetArchitecture.Arm64 => "ARM64",
        _ => "x64 (Intel/AMD)",
    };

    /// <summary>
    /// 解析依赖时的平台优先级（对应原版 dependencyPlatformPreference 的 Windows 版）。
    /// ARM64 优先 winarm64，回退 win64；x64 只用 win64。
    /// </summary>
    public static IReadOnlyList<string> DependencyPlatformPreference(this TargetArchitecture arch, string preferred)
    {
        var normalized = preferred.Trim().ToLowerInvariant();
        return arch switch
        {
            TargetArchitecture.Arm64 => normalized switch
            {
                "win64" => new[] { "win64" },
                _ => new[] { "winarm64", "win64" },
            },
            _ => new[] { "win64" },
        };
    }
}
