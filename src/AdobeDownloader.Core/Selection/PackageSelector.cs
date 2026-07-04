using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core.Selection;

/// <summary>
/// 依据语言/架构/Condition 选择需要下载的包，移植原版 download 工作流的选择逻辑：
/// eligible = Condition 满足 &amp;&amp; 处理器架构兼容；
/// selected = eligible &amp;&amp; (core 包 或 属于某模块 或 该产品无模块)。
/// </summary>
public static class PackageSelector
{
    public static List<AppPackage> Select(
        ApplicationInfo app, string installLanguage, TargetArchitecture arch)
    {
        var vars = BuildVariables(installLanguage, arch);
        var hasModules = app.Modules.Count > 0;

        var result = new List<AppPackage>();
        foreach (var pkg in app.Packages)
        {
            var isCore = pkg.Type.Equals("core", StringComparison.OrdinalIgnoreCase);

            if (!IsArchitectureCompatible(pkg.ProcessorFamily, arch)) continue;
            if (!ConditionEvaluator.Evaluate(pkg.Condition, vars)) continue;

            var belongsToModule = app.Modules.Any(m =>
                m.ReferencePackages.Contains(pkg.PackageName) ||
                m.ReferencePackages.Contains(pkg.FullPackageName));

            var selected = isCore || !hasModules || belongsToModule;
            if (selected) result.Add(pkg);
        }
        return result;
    }

    /// <summary>
    /// 处理器架构兼容性。Windows 上 Adobe 的 ProcessorFamily 常为 "64-bit"（通用 64 位），
    /// 也可能是 arm/arm64 或 x64/x86。空=通用。
    /// </summary>
    public static bool IsArchitectureCompatible(string processorFamily, TargetArchitecture arch)
    {
        if (string.IsNullOrWhiteSpace(processorFamily)) return true;
        var pf = processorFamily.Trim().ToLowerInvariant();

        if (pf.Contains("arm"))
            return arch == TargetArchitecture.Arm64;
        // "64-bit" 视为对两种 64 位架构均兼容
        if (pf.Contains("64-bit") || pf == "64")
            return true;
        return arch switch
        {
            TargetArchitecture.Arm64 => pf.Contains("arm"),
            _ => pf.Contains("x64") || pf.Contains("x86") || pf.Contains("intel") || pf.Contains("amd"),
        };
    }

    private static Dictionary<string, string> BuildVariables(string installLanguage, TargetArchitecture arch)
    {
        var os = Environment.OSVersion.Version;
        return new Dictionary<string, string>
        {
            ["installLanguage"] = installLanguage,
            ["OSVersion"] = $"{os.Major}.{os.Minor}",
            // Adobe Windows 约定：OSArchitecture=x64/arm64，OSProcessorFamily=64-bit
            ["OSArchitecture"] = arch.DriverValue(),
            ["OSProcessorFamily"] = "64-bit",
            ["IsEnterpriseDeployment"] = "false",
        };
    }
}
