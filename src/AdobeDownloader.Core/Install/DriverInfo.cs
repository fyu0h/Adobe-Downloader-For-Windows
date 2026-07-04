using System.Xml.Linq;

namespace AdobeDownloader.Core.Install;

/// <summary>解析回 driver.xml（安装时读取），对应生成时的 DriverXmlGenerator。</summary>
public sealed class DriverInfo
{
    public DriverComponent Product { get; set; } = new();
    public List<DriverComponent> Dependencies { get; set; } = new();

    public string InstallDir { get; set; } = "";        // 如 C:\Program Files\Adobe
    public string InstallLanguage { get; set; } = "";   // 如 zh_CN
    public string TargetArchitecture { get; set; } = "";// x64 / arm64

    /// <summary>主产品 + 依赖，按安装顺序（依赖在前）。</summary>
    public IEnumerable<DriverComponent> AllComponentsInInstallOrder()
        => Dependencies.Concat(new[] { Product });

    public static DriverInfo Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root ?? throw new FormatException("driver.xml 根节点缺失");
        var productInfo = root.Element("ProductInfo") ?? throw new FormatException("driver.xml 缺少 ProductInfo");
        var requestInfo = root.Element("RequestInfo");

        var info = new DriverInfo
        {
            Product = ParseComponent(productInfo),
            InstallDir = requestInfo?.Element("InstallDir")?.Value.Trim() ?? "",
            InstallLanguage = requestInfo?.Element("InstallLanguage")?.Value.Trim() ?? "",
            TargetArchitecture = requestInfo?.Element("TargetArchitecture")?.Value.Trim() ?? "",
        };

        var deps = productInfo.Element("Dependencies");
        if (deps is not null)
            foreach (var d in deps.Elements("Dependency"))
                info.Dependencies.Add(ParseComponent(d));

        return info;
    }

    private static DriverComponent ParseComponent(XElement e) => new()
    {
        SapCode = e.Element("SapCode")?.Value.Trim() ?? "",
        CodexVersion = e.Element("CodexVersion")?.Value.Trim() ?? "",
        BaseVersion = e.Element("BaseVersion")?.Value.Trim() ?? "",
        BuildVersion = e.Element("BuildVersion")?.Value.Trim() ?? "",
        EsdDirectory = e.Element("EsdDirectory")?.Value.Trim() ?? "",
        Platform = e.Element("Platform")?.Value.Trim() ?? "",
        BuildGuid = e.Element("BuildGuid")?.Value.Trim() ?? "",
    };
}

public sealed class DriverComponent
{
    public string SapCode { get; set; } = "";
    public string CodexVersion { get; set; } = "";
    public string BaseVersion { get; set; } = "";
    public string BuildVersion { get; set; } = "";
    public string EsdDirectory { get; set; } = "";
    public string Platform { get; set; } = "";
    public string BuildGuid { get; set; } = "";
}
