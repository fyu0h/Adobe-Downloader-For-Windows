using AdobeDownloader.Core;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class ProductVersionNamingTests
{
    [Theory]
    [InlineData("ILST", "30.6", 2026)]   // Illustrator 偏移 1996
    [InlineData("ILST", "28.0", 2024)]
    [InlineData("PHSP", "27.0", 2026)]   // Photoshop 偏移 1999
    [InlineData("PHSP", "25.0", 2024)]
    [InlineData("AEFT", "26.3", 2026)]   // After Effects 偏移 2000
    [InlineData("PPRO", "24.0", 2024)]
    [InlineData("AME", "26.0.2", 2026)]
    [InlineData("KBRG", "16.0.4", 2026)] // Bridge 偏移 2010
    [InlineData("IDSN", "19.0", 2024)]   // InDesign 偏移 2005
    public void Year_MapsKnownProducts(string sap, string ver, int expected)
        => Assert.Equal(expected, ProductVersionNaming.Year(sap, ver));

    [Fact]
    public void Year_UnknownSapCode_ReturnsNull()
        => Assert.Null(ProductVersionNaming.Year("XXXX", "12.1.0"));

    [Fact]
    public void Year_StripsBetaSuffix()
        => Assert.Equal(2026, ProductVersionNaming.Year("PPROBETA", "26.5"));

    [Theory]
    [InlineData("ILST", "30.6", "2026 (30.6)")]
    [InlineData("PPROBETA", "26.5", "2026 (26.5) Beta")]
    [InlineData("XXXX", "12.1.0", "12.1.0")]        // 未知产品只显示版本
    public void Label_FormatsForDisplay(string sap, string ver, string expected)
        => Assert.Equal(expected, ProductVersionNaming.Label(sap, ver));
}
