using System.Xml.Linq;
using AdobeDownloader.Core;
using AdobeDownloader.Core.Models;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class DriverXmlGeneratorTests
{
    private static DownloadPlan SamplePlan() => new()
    {
        ProductId = "PHSP",
        ProductVersion = "26.0.0",
        Language = "zh_CN",
        Platform = "win64",
        Architecture = TargetArchitecture.X64,
        Components =
        {
            new PlanComponent
            {
                SapCode = "PHSP", Version = "26.0.0", BaseVersion = "26.0.0", BuildVersion = "26.0.0",
                Platform = "win64", BuildGuid = "main-guid", IsMainProduct = true,
            },
            new PlanComponent
            {
                SapCode = "ACR", Version = "17.0", BaseVersion = "17.0", BuildVersion = "17.0",
                Platform = "win64", BuildGuid = "dep-guid", IsMainProduct = false,
            },
        },
    };

    [Fact]
    public void Generate_ProducesValidXmlWithWindowsPlatform()
    {
        var xml = DriverXmlGenerator.Generate(SamplePlan(), @"C:\Program Files\Adobe");
        var doc = XDocument.Parse(xml);

        var productInfo = doc.Root!.Element("ProductInfo")!;
        Assert.Equal("PHSP", productInfo.Element("SapCode")!.Value);
        Assert.Equal("win64", productInfo.Element("Platform")!.Value);
        Assert.Equal("main-guid", productInfo.Element("BuildGuid")!.Value);

        var dep = Assert.Single(productInfo.Element("Dependencies")!.Elements("Dependency"));
        Assert.Equal("ACR", dep.Element("SapCode")!.Value);
        Assert.Equal("dep-guid", dep.Element("BuildGuid")!.Value);

        var requestInfo = doc.Root!.Element("RequestInfo")!;
        Assert.Equal(@"C:\Program Files\Adobe", requestInfo.Element("InstallDir")!.Value);
        Assert.Equal("zh_CN", requestInfo.Element("InstallLanguage")!.Value);
        Assert.Equal("x64", requestInfo.Element("TargetArchitecture")!.Value);
    }

    [Fact]
    public void Generate_Arm64_UsesArm64Architecture()
    {
        var plan = SamplePlan();
        plan.Architecture = TargetArchitecture.Arm64;
        var doc = XDocument.Parse(DriverXmlGenerator.Generate(plan));
        Assert.Equal("arm64", doc.Root!.Element("RequestInfo")!.Element("TargetArchitecture")!.Value);
    }
}
