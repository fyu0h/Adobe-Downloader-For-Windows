using AdobeDownloader.Core;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class DownloadEngineTests
{
    [Theory]
    [InlineData("/products/PHSP/core.zip", "https://cdn.example.com/products/PHSP/core.zip")]
    [InlineData("products/PHSP/core.zip", "https://cdn.example.com/products/PHSP/core.zip")]
    [InlineData("https://other.example.com/a.zip", "https://other.example.com/a.zip")]
    [InlineData("http://other.example.com/a.zip", "http://other.example.com/a.zip")]
    public void NormalizeUrl_HandlesRelativeAndAbsolute(string input, string expected)
    {
        var engine = new DownloadEngine("https://cdn.example.com/");
        Assert.Equal(expected, engine.NormalizeUrl(input));
    }
}
