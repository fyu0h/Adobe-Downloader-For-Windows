using AdobeDownloader.Core.Models;
using AdobeDownloader.Core.Selection;

namespace AdobeDownloader.Core;

/// <summary>
/// 由目录中的一个 Product + 用户选择（版本/语言/架构）构建完整 DownloadPlan：
/// 为主产品及其每个依赖抓取 application.json，选出需下载的包。
/// </summary>
public sealed class PlanBuilder
{
    private readonly AdobeApiClient _api;
    private readonly DependencyResolver? _resolver;

    public PlanBuilder(AdobeApiClient api, IEnumerable<Product>? dependencyPool = null)
    {
        _api = api;
        _resolver = dependencyPool is null ? null : new DependencyResolver(dependencyPool);
    }

    /// <summary>为指定架构挑选平台及其首个语言集。找不到则返回 null。</summary>
    public static (ProductPlatform platform, LanguageSet languageSet)? ResolvePlatform(
        Product product, TargetArchitecture arch)
    {
        foreach (var platformId in arch.CatalogPlatformIds())
        {
            var platform = product.Platforms.FirstOrDefault(p => p.Id == platformId);
            var ls = platform?.LanguageSets.FirstOrDefault();
            if (platform is not null && ls is not null)
                return (platform, ls);
        }
        return null;
    }

    public async Task<DownloadPlan> BuildAsync(
        Product product, string language, TargetArchitecture arch,
        IProgress<string>? status = null, CancellationToken ct = default)
    {
        var resolved = ResolvePlatform(product, arch)
            ?? throw new AdobeApiException($"{product.Id} 在架构 {arch.PlatformId()} 下没有可用平台");

        var (platform, languageSet) = resolved;

        var plan = new DownloadPlan
        {
            ProductId = product.Id,
            ProductVersion = product.Version,
            DisplayName = product.DisplayName,
            Language = language,
            Platform = platform.Id,
            Architecture = arch,
        };

        // 主产品
        status?.Report($"获取 {product.Id} {product.Version} 的包信息...");
        var mainComponent = await BuildComponentAsync(
            sapCode: product.Id,
            version: product.Version,
            baseVersion: languageSet.BaseVersion,
            buildGuid: languageSet.BuildGuid,
            platformId: platform.Id,
            language: language, arch: arch, isMain: true, ct: ct);
        plan.Components.Add(mainComponent);

        // 依赖：ccm 目录里依赖往往没有 buildGuid，需从产品池（含 sti 频道）解析
        var seenSap = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { product.Id };
        foreach (var dep in languageSet.Dependencies)
        {
            if (string.IsNullOrEmpty(dep.SapCode) || !seenSap.Add(dep.SapCode))
                continue;

            var resolvedDep = _resolver?.Resolve(dep, arch);
            var buildGuid = resolvedDep?.BuildGuid ?? dep.BuildGuid;
            if (string.IsNullOrEmpty(buildGuid))
            {
                status?.Report($"依赖 {dep.SapCode} 无法解析构建信息，已跳过");
                continue;
            }

            var depPlatform = resolvedDep?.Platform
                ?? (string.IsNullOrEmpty(dep.SelectedPlatform) ? platform.Id : dep.SelectedPlatform);
            var depVersion = resolvedDep?.Version
                ?? (string.IsNullOrEmpty(dep.ProductVersion) ? dep.BaseVersion : dep.ProductVersion);

            status?.Report($"获取依赖 {dep.SapCode} 的包信息...");
            var depComponent = await BuildComponentAsync(
                sapCode: dep.SapCode,
                version: depVersion,
                baseVersion: resolvedDep?.BaseVersion ?? dep.BaseVersion,
                buildGuid: buildGuid,
                platformId: depPlatform,
                language: language, arch: arch, isMain: false, ct: ct);
            plan.Components.Add(depComponent);
        }

        return plan;
    }

    private async Task<PlanComponent> BuildComponentAsync(
        string sapCode, string version, string baseVersion, string buildGuid, string platformId,
        string language, TargetArchitecture arch, bool isMain, CancellationToken ct)
    {
        var app = await _api.FetchApplicationInfoAsync(sapCode, version, platformId, buildGuid, ct);

        var component = new PlanComponent
        {
            SapCode = sapCode,
            Version = string.IsNullOrEmpty(app.CodexVersion) ? version : app.CodexVersion,
            BaseVersion = string.IsNullOrEmpty(app.BaseVersion) ? baseVersion : app.BaseVersion,
            BuildVersion = string.IsNullOrEmpty(app.ProductVersion) ? version : app.ProductVersion,
            Platform = platformId,
            BuildGuid = buildGuid,
            IsMainProduct = isMain,
        };

        foreach (var pkg in PackageSelector.Select(app, language, arch))
        {
            component.Packages.Add(new DownloadPackage
            {
                FullPackageName = pkg.FullPackageName,
                Type = pkg.Type,
                DownloadPath = pkg.Path,
                DownloadSize = pkg.DownloadSize,
                PackageVersion = string.IsNullOrEmpty(pkg.PackageVersion) ? version : pkg.PackageVersion,
            });
        }

        return component;
    }
}
