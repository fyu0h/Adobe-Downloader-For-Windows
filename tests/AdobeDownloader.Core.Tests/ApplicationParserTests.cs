using AdobeDownloader.Core.Parsing;
using AdobeDownloader.Core.Selection;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class ApplicationParserTests
{
    // Packages.Package 为数组
    private const string ArrayJson = """
    {
      "SAPCode": "PHSP",
      "ProductVersion": "26.0.0",
      "BaseVersion": "26.0.0",
      "SupportedLanguages": { "Language": [ {"locale":"zh_CN"}, {"locale":"en_US"} ] },
      "Packages": {
        "Package": [
          { "PackageName":"AdobePhotoshop26-Core","Type":"core","Path":"/PHSP/core.zip","DownloadSize":"1000" },
          { "PackageName":"AdobePhotoshop26-zh","Type":"noncore","Condition":"[installLanguage]==zh_CN","Path":"/PHSP/zh.zip","DownloadSize":500 },
          { "PackageName":"AdobePhotoshop26-en","Type":"noncore","Condition":"[installLanguage]==en_US","Path":"/PHSP/en.zip","DownloadSize":500 }
        ]
      }
    }
    """;

    // Packages.Package 退化为单个对象
    private const string SingleJson = """
    {
      "SAPCode": "ACR",
      "ProductVersion": "17.0",
      "Packages": { "Package": { "PackageName":"ACR-Core","Type":"core","Path":"/ACR/core.zip","DownloadSize":200 } }
    }
    """;

    [Fact]
    public void Parse_ArrayPackages()
    {
        var info = ApplicationParser.Parse(ArrayJson);
        Assert.Equal("PHSP", info.SapCode);
        Assert.Equal(3, info.Packages.Count);
        Assert.Equal(2, info.SupportedLanguages.Count);
        Assert.Equal(1000, info.Packages[0].DownloadSize);
        Assert.Equal("AdobePhotoshop26-Core.zip", info.Packages[0].FullPackageName);
    }

    [Fact]
    public void Parse_SinglePackageObject()
    {
        var info = ApplicationParser.Parse(SingleJson);
        var pkg = Assert.Single(info.Packages);
        Assert.Equal("ACR-Core", pkg.PackageName);
        Assert.Equal(200, pkg.DownloadSize);
    }

    [Fact]
    public void Select_FiltersByLanguageCondition()
    {
        var info = ApplicationParser.Parse(ArrayJson);

        var zh = PackageSelector.Select(info, "zh_CN", TargetArchitecture.X64);
        Assert.Equal(2, zh.Count); // core + zh noncore
        Assert.Contains(zh, p => p.PackageName == "AdobePhotoshop26-Core");
        Assert.Contains(zh, p => p.PackageName == "AdobePhotoshop26-zh");
        Assert.DoesNotContain(zh, p => p.PackageName == "AdobePhotoshop26-en");

        var en = PackageSelector.Select(info, "en_US", TargetArchitecture.X64);
        Assert.Equal(2, en.Count);
        Assert.Contains(en, p => p.PackageName == "AdobePhotoshop26-en");
    }

    [Fact]
    public void Select_AllLanguage_IncludesEverything()
    {
        var info = ApplicationParser.Parse(ArrayJson);
        var all = PackageSelector.Select(info, "ALL", TargetArchitecture.X64);
        Assert.Equal(3, all.Count);
    }

    // Windows 约定：ProcessorFamily="64-bit"、Condition=[OSProcessorFamily]==64-bit
    private const string WindowsRuntimeJson = """
    {
      "SAPCode": "VC14win64",
      "ProductVersion": "2.0.0.2",
      "Packages": { "Package": {
        "PackageName":"VCRedist14-64","Type":"core","ProcessorFamily":"64-bit",
        "Condition":"[OSProcessorFamily]==64-bit","Path":"/VC/redist.zip","DownloadSize":18307193
      } }
    }
    """;

    [Fact]
    public void Select_Windows64BitPackage_IsIncluded()
    {
        var info = ApplicationParser.Parse(WindowsRuntimeJson);
        var x64 = PackageSelector.Select(info, "ALL", TargetArchitecture.X64);
        Assert.Single(x64);
        var arm = PackageSelector.Select(info, "ALL", TargetArchitecture.Arm64);
        Assert.Single(arm); // "64-bit" 对两种架构均兼容
    }

    [Theory]
    [InlineData("64-bit", TargetArchitecture.X64, true)]
    [InlineData("64-bit", TargetArchitecture.Arm64, true)]
    [InlineData("arm64", TargetArchitecture.X64, false)]
    [InlineData("arm64", TargetArchitecture.Arm64, true)]
    [InlineData("", TargetArchitecture.X64, true)]
    public void IsArchitectureCompatible_Cases(string pf, TargetArchitecture arch, bool expected)
        => Assert.Equal(expected, PackageSelector.IsArchitectureCompatible(pf, arch));
}
