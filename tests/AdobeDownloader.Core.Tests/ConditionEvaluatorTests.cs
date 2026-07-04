using AdobeDownloader.Core.Selection;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class ConditionEvaluatorTests
{
    private static Dictionary<string, string> Vars(string lang = "zh_CN", string osVer = "10.0", string arch = "x64")
        => new()
        {
            ["installLanguage"] = lang,
            ["OSVersion"] = osVer,
            ["OSArchitecture"] = arch,
            ["OSProcessorFamily"] = arch,
            ["IsEnterpriseDeployment"] = "false",
        };

    [Fact]
    public void EmptyCondition_IsSatisfied()
    {
        Assert.True(ConditionEvaluator.Evaluate("", Vars()));
        Assert.True(ConditionEvaluator.Evaluate("   ", Vars()));
    }

    [Fact]
    public void LanguageEquality_Matches()
    {
        Assert.True(ConditionEvaluator.Evaluate("[installLanguage]==zh_CN", Vars(lang: "zh_CN")));
        Assert.False(ConditionEvaluator.Evaluate("[installLanguage]==en_US", Vars(lang: "zh_CN")));
    }

    [Fact]
    public void AllLanguage_AlwaysMatches()
    {
        Assert.True(ConditionEvaluator.Evaluate("[installLanguage]==zh_CN", Vars(lang: "ALL")));
    }

    [Fact]
    public void CommaList_OverlapMatches()
    {
        Assert.True(ConditionEvaluator.Evaluate("[installLanguage]==en_US,zh_CN,fr_FR", Vars(lang: "zh_CN")));
        Assert.False(ConditionEvaluator.Evaluate("[installLanguage]==en_US,fr_FR", Vars(lang: "zh_CN")));
    }

    [Fact]
    public void VersionComparison_Works()
    {
        Assert.True(ConditionEvaluator.Evaluate("[OSVersion]>=10.0", Vars(osVer: "10.0")));
        Assert.True(ConditionEvaluator.Evaluate("[OSVersion]>=10.0", Vars(osVer: "10.15")));
        Assert.False(ConditionEvaluator.Evaluate("[OSVersion]>=11.0", Vars(osVer: "10.0")));
    }

    [Fact]
    public void AndOrNot_Compose()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            "[OSArchitecture]==x64 && [installLanguage]==zh_CN", Vars(arch: "x64", lang: "zh_CN")));
        Assert.False(ConditionEvaluator.Evaluate(
            "[OSArchitecture]==arm64 && [installLanguage]==zh_CN", Vars(arch: "x64")));
        Assert.True(ConditionEvaluator.Evaluate(
            "[OSArchitecture]==arm64 || [OSArchitecture]==x64", Vars(arch: "x64")));
        Assert.True(ConditionEvaluator.Evaluate(
            "!([OSArchitecture]==arm64)", Vars(arch: "x64")));
    }

    [Fact]
    public void Parentheses_GroupCorrectly()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            "[OSVersion]>=10.0 && ([OSArchitecture]==x64 || [OSArchitecture]==arm64)",
            Vars(osVer: "10.0", arch: "arm64")));
    }

    [Fact]
    public void AmpEntity_IsNormalized()
    {
        Assert.True(ConditionEvaluator.Evaluate(
            "[OSArchitecture]==x64 &amp;&amp; [installLanguage]==zh_CN", Vars(arch: "x64", lang: "zh_CN")));
    }
}
