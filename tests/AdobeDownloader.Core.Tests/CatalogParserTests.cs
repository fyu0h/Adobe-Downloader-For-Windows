using AdobeDownloader.Core.Parsing;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class CatalogParserTests
{
    private const string SampleXml = """
    <response>
      <channels>
        <channel name="ccm">
          <cdn><secure>https://cdn.example.com</secure></cdn>
          <products>
            <product id="PHSP" version="26.0.0">
              <displayName>Adobe Photoshop</displayName>
              <type>desktop</type>
              <family>Photoshop</family>
              <productIcons>
                <icon size="192x192">https://icons.example.com/ps.png</icon>
                <icon size="96x96">https://icons.example.com/ps-small.png</icon>
              </productIcons>
              <platforms>
                <platform id="win64">
                  <languageSet name="zh_CN" productCode="PHSP-26.0.0" installSize="3500000000"
                              buildGuid="abc-guid-123" baseVersion="26.0.0" productVersion="26.0.0">
                    <urls>
                      <manifestURL>/products/PHSP/manifest.xml</manifestURL>
                      <lbsURL>/products/PHSP/lbs</lbsURL>
                    </urls>
                    <dependencies>
                      <dependency>
                        <sapCode>ACR</sapCode>
                        <baseVersion>17.0</baseVersion>
                        <productVersion>17.0</productVersion>
                        <buildGuid>dep-guid-456</buildGuid>
                      </dependency>
                    </dependencies>
                  </languageSet>
                </platform>
              </platforms>
              <referencedProducts>
                <referencedProduct><sapCode>ACR</sapCode><version>17.0</version></referencedProduct>
              </referencedProducts>
            </product>
          </products>
        </channel>
        <channel name="sti">
          <cdn><secure>https://cdn.example.com</secure></cdn>
          <products/>
        </channel>
      </channels>
    </response>
    """;

    [Fact]
    public void Parse_ExtractsCdnAndProduct()
    {
        var result = CatalogParser.Parse(SampleXml);

        Assert.Equal("https://cdn.example.com", result.Cdn);
        var product = Assert.Single(result.Products);
        Assert.Equal("PHSP", product.Id);
        Assert.Equal("26.0.0", product.Version);
        Assert.Equal("Adobe Photoshop", product.DisplayName);
    }

    [Fact]
    public void Parse_ExtractsPlatformLanguageSetAndDependencies()
    {
        var product = CatalogParser.Parse(SampleXml).Products.Single();

        var platform = Assert.Single(product.Platforms);
        Assert.Equal("win64", platform.Id);

        var ls = Assert.Single(platform.LanguageSets);
        Assert.Equal("abc-guid-123", ls.BuildGuid);
        Assert.Equal(3_500_000_000, ls.InstallSize);
        Assert.Equal("/products/PHSP/manifest.xml", ls.ManifestUrl);

        var dep = Assert.Single(ls.Dependencies);
        Assert.Equal("ACR", dep.SapCode);
        Assert.Equal("dep-guid-456", dep.BuildGuid);
    }

    [Fact]
    public void Parse_PicksBestIcon()
    {
        var product = CatalogParser.Parse(SampleXml).Products.Single();
        var icon = product.GetBestIcon();
        Assert.NotNull(icon);
        Assert.Equal("192x192", icon!.Size);
    }
}
