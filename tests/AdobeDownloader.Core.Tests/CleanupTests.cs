using AdobeDownloader.Core.Cleanup;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class CleanupSafetyTests
{
    [Theory]
    // 允许：明确的 Adobe 子路径
    [InlineData(@"C:\Program Files\Adobe\Adobe Photoshop 2026", true)]
    [InlineData(@"C:\Program Files (x86)\Adobe\Acrobat", true)]
    [InlineData(@"C:\ProgramData\Adobe\SLStore", true)]
    // 禁止：盘根与系统目录
    [InlineData(@"C:\", false)]
    [InlineData(@"C:\Windows", false)]
    [InlineData(@"C:\Windows\System32", false)]
    [InlineData(@"C:\Program Files", false)]
    [InlineData(@"C:\Program Files (x86)", false)]
    [InlineData(@"C:\Users", false)]
    // 禁止：Adobe 根目录（只允许删子项，不允许一次删整个 Adobe 根）
    [InlineData(@"C:\Program Files\Adobe", false)]
    // 禁止：非 Adobe 路径
    [InlineData(@"C:\Temp\something", false)]
    [InlineData(@"C:\Users\Public\Documents\Report.docx", false)]
    // 禁止：本工具自身/下载目录（含 adobedownloader，忽略空格/连字符/下划线）
    [InlineData(@"C:\Users\PC\Downloads\AdobeDownloader", false)]
    [InlineData(@"C:\Users\PC\Downloads\AdobeDownloader\KBRG_16.0.4", false)]
    [InlineData(@"D:\code\adobe_downloader", false)]
    public void IsSafeToDelete_Cases(string path, bool expected)
        => Assert.Equal(expected, CleanupSafety.IsSafeToDelete(path));

    [Fact]
    public void EmptyPath_IsNotSafe()
    {
        Assert.False(CleanupSafety.IsSafeToDelete(""));
        Assert.False(CleanupSafety.IsSafeToDelete("   "));
    }
}

public class CleanupOptionTests
{
    [Fact]
    public void ExecutionOrder_CoversAllOptions()
    {
        var all = Enum.GetValues<CleanupOption>().ToHashSet();
        Assert.Equal(all, CleanupOptionExtensions.ExecutionOrder.ToHashSet());
    }

    [Fact]
    public void EveryOption_HasTargetsAndNames()
    {
        foreach (var o in Enum.GetValues<CleanupOption>())
        {
            Assert.NotEmpty(o.DisplayName());
            Assert.NotEmpty(o.Targets());
        }
    }

    [Fact]
    public void ServicesAndHosts_UseCorrectKinds()
    {
        Assert.Contains(CleanupOption.AdobeServices.Targets(), t => t.Kind == CleanupActionKind.Service);
        Assert.Contains(CleanupOption.AdobeHosts.Targets(), t => t.Kind == CleanupActionKind.HostsClean);
        Assert.Contains(CleanupOption.AdobePreferences.Targets(), t => t.Kind == CleanupActionKind.RegistryKey);
    }
}

public class HostsLineTests
{
    [Theory]
    [InlineData("127.0.0.1 lm.licenses.adobe.com", true)]
    [InlineData("0.0.0.0 activate.adobe.com", true)]
    [InlineData("127.0.0.1  practivate.adobe.com", true)]
    [InlineData("# this is a comment about adobe", false)]
    [InlineData("127.0.0.1 localhost", false)]
    [InlineData("", false)]
    public void IsAdobeHostsLine_Cases(string line, bool expected)
        => Assert.Equal(expected, CleanupHosts.IsAdobeHostsLine(line));
}
