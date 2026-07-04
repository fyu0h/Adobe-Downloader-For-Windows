using System.Text.Json;
using System.Text.Json.Serialization;
using AdobeDownloader.Core;
using AdobeDownloader.Core.Models;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class DownloadPlanSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void RoundTrip_PreservesTaskData()
    {
        var plan = new DownloadPlan
        {
            ProductId = "KBRG",
            ProductVersion = "16.0.4",
            DisplayName = "Adobe Bridge",
            Language = "zh_CN",
            Platform = "win64",
            Architecture = TargetArchitecture.X64,
            Cdn = "https://ccmdls.adobe.com",
            Components =
            {
                new PlanComponent
                {
                    SapCode = "KBRG", Version = "16.0.4", BuildGuid = "guid-1", IsMainProduct = true,
                    Packages =
                    {
                        new DownloadPackage
                        {
                            FullPackageName = "AdobeBridge16.0-mul-x64.zip",
                            DownloadPath = "/AdobeProducts/KBRG/16.0.4/win64/x/AdobeBridge16.0-mul-x64.zip",
                            DownloadSize = 678900000, Type = "core",
                        },
                    },
                },
            },
        };

        var json = JsonSerializer.Serialize(plan, Options);
        var back = JsonSerializer.Deserialize<DownloadPlan>(json, Options)!;

        Assert.Equal("KBRG", back.ProductId);
        Assert.Equal("16.0.4", back.ProductVersion);
        Assert.Equal("https://ccmdls.adobe.com", back.Cdn);       // CDN 必须保留（恢复下载用）
        Assert.Equal(TargetArchitecture.X64, back.Architecture);
        var comp = Assert.Single(back.Components);
        Assert.True(comp.IsMainProduct);
        var pkg = Assert.Single(comp.Packages);
        Assert.Equal("AdobeBridge16.0-mul-x64.zip", pkg.FullPackageName);
        Assert.Equal(678900000, pkg.DownloadSize);
        Assert.Equal("/AdobeProducts/KBRG/16.0.4/win64/x/AdobeBridge16.0-mul-x64.zip", pkg.DownloadPath);
    }
}
