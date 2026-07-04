namespace AdobeDownloader.Core.Models;

/// <summary>
/// Adobe 产品目录中的单个产品（对应原版 Product 结构）。
/// </summary>
public sealed class Product
{
    public string Id { get; set; } = "";            // SapCode，例如 PHSP
    public string DisplayName { get; set; } = "";
    public string Version { get; set; } = "";       // 目录中的产品版本
    public string Type { get; set; } = "";
    public string Family { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string AppLineage { get; set; } = "";
    public bool Hidden { get; set; }

    public List<ProductIcon> Icons { get; set; } = new();
    public List<ProductPlatform> Platforms { get; set; } = new();
    public List<ReferencedProduct> ReferencedProducts { get; set; } = new();

    /// <summary>供 UI 显示的版本标签，含大众熟知的年份，如 “2026 (30.6)”。</summary>
    public string VersionDisplay => ProductVersionNaming.Label(Id, Version);

    /// <summary>是否有在指定平台下可用的语言集。</summary>
    public bool HasValidVersions(IEnumerable<string> allowedPlatforms)
        => Platforms.Any(p => allowedPlatforms.Contains(p.Id) && p.LanguageSets.Count > 0);

    public ProductIcon? GetBestIcon()
        => Icons.FirstOrDefault(i => i.Size == "192x192")
           ?? Icons.OrderByDescending(i => i.Dimension).FirstOrDefault();
}

public sealed class ProductIcon
{
    public string Value { get; set; } = "";
    public string Size { get; set; } = "";

    public int Dimension
    {
        get
        {
            var parts = Size.Split('x');
            return parts.Length == 2 && int.TryParse(parts[0], out var d) ? d : 0;
        }
    }
}

public sealed class ReferencedProduct
{
    public string SapCode { get; set; } = "";
    public string Version { get; set; } = "";
}

public sealed class ProductPlatform
{
    public string Id { get; set; } = "";           // win64 / winarm64
    public List<LanguageSet> LanguageSets { get; set; } = new();
    public List<ProductModule> Modules { get; set; } = new();
    public List<VersionRange> Ranges { get; set; } = new();
}

public sealed class LanguageSet
{
    public string ManifestUrl { get; set; } = "";
    public string LbsUrl { get; set; } = "";
    public string ProductCode { get; set; } = "";
    public string Name { get; set; } = "";
    public long InstallSize { get; set; }
    public string BuildGuid { get; set; } = "";
    public string BaseVersion { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public List<Dependency> Dependencies { get; set; } = new();
}

public sealed class Dependency
{
    public string SapCode { get; set; } = "";
    public string BaseVersion { get; set; } = "";
    public string ProductVersion { get; set; } = "";
    public string BuildGuid { get; set; } = "";
    public string SelectedPlatform { get; set; } = "";
    public string TargetPlatform { get; set; } = "";
    public bool IsSoftDependency { get; set; }
}

public sealed class ProductModule
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string DeploymentType { get; set; } = "";
}

public sealed class VersionRange
{
    public string Min { get; set; } = "";
    public string Max { get; set; } = "";
}

/// <summary>目录解析结果：可见产品列表 + 依赖解析用的全量产品池 + CDN 基址。</summary>
public sealed class CatalogResult
{
    /// <summary>ccm 频道的可见产品，供 UI 展示。</summary>
    public List<Product> Products { get; set; } = new();

    /// <summary>ccm+sti+nocc 全部产品，供依赖 buildGuid 解析（含隐藏依赖产品，如 ACR/CCXP）。</summary>
    public List<Product> DependencyPool { get; set; } = new();

    public string Cdn { get; set; } = "";
}
