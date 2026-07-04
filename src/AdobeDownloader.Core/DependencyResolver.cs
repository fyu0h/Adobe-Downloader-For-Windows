using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core;

/// <summary>
/// 从目录产品池中解析依赖的构建信息（buildGuid/platform/version），
/// 移植原版 selectPreferredDependencyProduct / resolveDependencySeed。
/// 依赖产品通常来自隐藏的 sti 频道（如 ACR、CCXP、COSY）。
/// </summary>
public sealed class DependencyResolver
{
    private readonly List<Product> _pool;

    public DependencyResolver(IEnumerable<Product> dependencyPool) => _pool = dependencyPool.ToList();

    /// <summary>解析后的依赖构建信息。</summary>
    public sealed record ResolvedDependency(
        string SapCode, string Version, string BaseVersion, string BuildGuid, string Platform);

    /// <summary>
    /// 解析一个依赖。找不到匹配产品时回退用依赖自带的 buildGuid（若有）。
    /// </summary>
    public ResolvedDependency? Resolve(Dependency dep, TargetArchitecture arch)
    {
        var match = SelectPreferredDependencyProduct(dep.SapCode, dep.BaseVersion, dep.SelectedPlatform, arch);

        if (match is null)
        {
            // 依赖自带 buildGuid 时仍可下载
            if (!string.IsNullOrEmpty(dep.BuildGuid))
            {
                var platform = string.IsNullOrEmpty(dep.SelectedPlatform) ? arch.PlatformId() : dep.SelectedPlatform;
                return new ResolvedDependency(dep.SapCode,
                    FirstNonEmpty(dep.ProductVersion, dep.BaseVersion), dep.BaseVersion, dep.BuildGuid, platform);
            }
            return null;
        }

        var (product, plat, ls) = match.Value;
        var buildGuid = FirstNonEmpty(dep.BuildGuid, ls.BuildGuid);
        if (string.IsNullOrEmpty(buildGuid)) return null;

        return new ResolvedDependency(
            SapCode: dep.SapCode,
            Version: FirstNonEmpty(dep.ProductVersion, ls.ProductVersion, product.Version),
            BaseVersion: FirstNonEmpty(dep.BaseVersion, ls.BaseVersion, product.Version),
            BuildGuid: buildGuid,
            Platform: plat.Id);
    }

    private (Product product, ProductPlatform platform, LanguageSet languageSet)? SelectPreferredDependencyProduct(
        string sapCode, string baseVersion, string preferredPlatform, TargetArchitecture arch)
    {
        var matching = _pool.Where(p => p.Id == sapCode).ToList();
        if (matching.Count == 0) return null;

        var exact = matching.Where(p =>
            p.Version == baseVersion ||
            p.Platforms.Any(pl => pl.LanguageSets.Any(l => l.BaseVersion == baseVersion))).ToList();

        var candidates = (exact.Count == 0 ? matching : exact)
            .OrderByDescending(p => p.Version, VersionComparer.Instance)
            .ToList();

        foreach (var product in candidates)
        {
            if (!string.IsNullOrEmpty(preferredPlatform))
            {
                var preferred = SelectPlatform(product, arch.DependencyPlatformPreference(preferredPlatform));
                if (preferred is not null) return (product, preferred.Value.platform, preferred.Value.ls);
            }

            var fallback = SelectPlatform(product, arch.DependencyPlatformPreference(""));
            if (fallback is not null) return (product, fallback.Value.platform, fallback.Value.ls);
        }
        return null;
    }

    private static (ProductPlatform platform, LanguageSet ls)? SelectPlatform(
        Product product, IReadOnlyList<string> preferenceOrder)
    {
        foreach (var id in preferenceOrder)
        {
            var platform = product.Platforms.FirstOrDefault(p => p.Id == id);
            var ls = platform?.LanguageSets.FirstOrDefault();
            if (platform is not null && ls is not null) return (platform, ls);
        }
        return null;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? x, string? y) => CompareVersions(x ?? "", y ?? "");

        private static int CompareVersions(string v1, string v2)
        {
            var a = v1.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var b = v2.Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var len = Math.Max(a.Length, b.Length);
            for (var i = 0; i < len; i++)
            {
                var p = i < a.Length ? a[i] : 0;
                var q = i < b.Length ? b[i] : 0;
                if (p != q) return p - q;
            }
            return 0;
        }
    }
}
