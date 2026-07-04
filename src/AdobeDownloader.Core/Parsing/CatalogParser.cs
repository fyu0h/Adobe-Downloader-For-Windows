using System.Xml.Linq;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core.Parsing;

/// <summary>
/// 解析 Adobe products/all 的 XML 响应（_type=xml），对应原版 NewJSONParser 的 XML 分支。
/// 返回可见的 ccm 频道产品；sti 频道为隐藏依赖，另行解析用于依赖查找。
/// </summary>
public static class CatalogParser
{
    /// <summary>
    /// 解析目录 XML。<paramref name="visibleChannel"/> 频道（默认 ccm）的产品作为可见列表，
    /// 所有频道（ccm+sti+nocc）的产品汇入依赖池以便解析依赖 buildGuid。
    /// </summary>
    public static CatalogResult Parse(string xml, string visibleChannel = "ccm")
    {
        var doc = XDocument.Parse(xml);
        var response = doc.Root ?? throw new FormatException("目录响应缺少根节点");

        var channels = response.Element("channels");
        if (channels is null)
            throw new FormatException("目录响应缺少 channels 节点");

        var channelList = channels.Elements("channel").ToList();

        var cdn = channelList
            .Select(c => c.Element("cdn")?.Element("secure")?.Value?.Trim())
            .FirstOrDefault(s => !string.IsNullOrEmpty(s));
        if (string.IsNullOrEmpty(cdn))
            throw new FormatException("目录响应缺少 CDN 地址");

        var visible = new List<Product>();
        var pool = new List<Product>();
        foreach (var channel in channelList)
        {
            var channelName = (string?)channel.Attribute("name") ?? "";
            var productsNode = channel.Element("products");
            if (productsNode is null) continue;

            foreach (var pe in productsNode.Elements("product"))
            {
                var product = ParseProduct(pe, channelName);
                if (product is null) continue;
                pool.Add(product);
                if (channelName == visibleChannel) visible.Add(product);
            }
        }

        return new CatalogResult { Products = visible, DependencyPool = pool, Cdn = cdn! };
    }

    private static Product? ParseProduct(XElement pe, string channelName)
    {
        var id = ((string?)pe.Attribute("id"))?.Trim() ?? "";
        var version = ((string?)pe.Attribute("version"))?.Trim() ?? "";
        var displayName = ChildText(pe, "displayName");

        if (id.Length == 0 || version.Length == 0 || displayName.Length == 0)
            return null;

        var product = new Product
        {
            Id = id,
            Version = version,
            DisplayName = displayName,
            Type = ChildText(pe, "type"),
            Family = ChildText(pe, "family"),
            AppLineage = ChildText(pe, "appLineage"),
            FamilyName = ChildText(pe, "familyName"),
            Hidden = channelName == "sti",
        };

        var icons = pe.Element("productIcons");
        if (icons is not null)
        {
            foreach (var icon in icons.Elements("icon"))
            {
                var size = ((string?)icon.Attribute("size"))?.Trim() ?? "";
                var value = icon.Value.Trim();
                if (size.Length > 0 && value.Length > 0)
                    product.Icons.Add(new ProductIcon { Size = size, Value = value });
            }
        }

        var platforms = pe.Element("platforms");
        if (platforms is not null)
        {
            foreach (var plat in platforms.Elements("platform"))
            {
                var parsed = ParsePlatform(plat);
                if (parsed is not null) product.Platforms.Add(parsed);
            }
        }

        var refs = pe.Element("referencedProducts");
        if (refs is not null)
        {
            foreach (var r in refs.Elements("referencedProduct"))
            {
                var sap = ChildText(r, "sapCode");
                var ver = ChildText(r, "version");
                if (sap.Length > 0 && ver.Length > 0)
                    product.ReferencedProducts.Add(new ReferencedProduct { SapCode = sap, Version = ver });
            }
        }

        return product;
    }

    private static ProductPlatform? ParsePlatform(XElement plat)
    {
        var id = ((string?)plat.Attribute("id"))?.Trim() ?? "";
        if (id.Length == 0) return null;

        var platform = new ProductPlatform { Id = id };

        foreach (var ls in plat.Elements("languageSet"))
            platform.LanguageSets.Add(ParseLanguageSet(ls));

        var modules = plat.Element("modules");
        if (modules is not null)
        {
            foreach (var m in modules.Elements("module"))
            {
                var mid = ((string?)m.Attribute("id"))?.Trim() ?? ChildText(m, "id");
                if (mid.Length == 0) continue;
                platform.Modules.Add(new ProductModule
                {
                    Id = mid,
                    DisplayName = ChildText(m, "displayName"),
                    DeploymentType = ChildText(m, "deploymentType"),
                });
            }
        }

        var ranges = plat.Element("systemCompatibility")?.Element("operatingSystem");
        if (ranges is not null)
        {
            foreach (var range in ranges.Elements("range"))
            {
                var raw = range.Value.Trim();
                if (raw.Length == 0) continue;
                var parts = raw.Split('-', 2);
                platform.Ranges.Add(new VersionRange
                {
                    Min = parts[0],
                    Max = parts.Length > 1 ? parts[1] : "",
                });
            }
        }

        return platform;
    }

    private static LanguageSet ParseLanguageSet(XElement ls)
    {
        var languageSet = new LanguageSet
        {
            ManifestUrl = NestedText(ls, "urls", "manifestURL"),
            LbsUrl = NestedText(ls, "urls", "lbsURL"),
            ProductCode = Attr(ls, "productCode"),
            Name = Attr(ls, "name"),
            InstallSize = long.TryParse(Attr(ls, "installSize"), out var sz) ? sz : 0,
            BuildGuid = Attr(ls, "buildGuid"),
            BaseVersion = Attr(ls, "baseVersion"),
            ProductVersion = Attr(ls, "productVersion"),
        };

        var deps = ls.Element("dependencies");
        if (deps is not null)
        {
            foreach (var d in deps.Elements("dependency"))
            {
                var sap = ChildText(d, "sapCode");
                var baseVer = ChildText(d, "baseVersion");
                if (sap.Length == 0 || baseVer.Length == 0) continue;
                languageSet.Dependencies.Add(new Dependency
                {
                    SapCode = sap,
                    BaseVersion = baseVer,
                    ProductVersion = ChildText(d, "productVersion"),
                    BuildGuid = ChildText(d, "buildGuid"),
                    SelectedPlatform = ChildText(d, "selectedPlatform"),
                });
            }
        }

        return languageSet;
    }

    /// <summary>
    /// 从目录 XML 中提取所有 dependencyFFCChannel 值（对应原版 extractDependencyChannelsFromXML）。
    /// 这些额外频道需二次请求才能获得依赖产品（如 ACR/CCXP/CORG）。
    /// </summary>
    public static HashSet<string> ExtractDependencyChannels(string xml)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        XDocument doc;
        try { doc = XDocument.Parse(xml); }
        catch { return result; }

        // /response/channels/channel/products/product/platforms/platform/custom-data/custom-entry[@key='dependencyFFCChannel']/value
        var entries = doc.Descendants("custom-entry")
            .Where(e => (string?)e.Attribute("key") == "dependencyFFCChannel");
        foreach (var entry in entries)
        {
            var value = entry.Element("value")?.Value.Trim();
            if (!string.IsNullOrEmpty(value)) result.Add(value);
        }
        return result;
    }

    private static string ChildText(XElement e, string name)
        => e.Element(name)?.Value.Trim() ?? "";

    private static string NestedText(XElement e, string parent, string child)
        => e.Element(parent)?.Element(child)?.Value.Trim() ?? "";

    private static string Attr(XElement e, string name)
        => ((string?)e.Attribute(name))?.Trim() ?? "";
}
