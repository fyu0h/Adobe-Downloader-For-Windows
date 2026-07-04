using AdobeDownloader.Core.Install;
using Xunit;

namespace AdobeDownloader.Core.Tests;

public class InstallPropertiesTests
{
    [Fact]
    public void ForWindows_ExpandsInstallDirFromAdobeProgramFiles()
    {
        var p = InstallProperties.ForWindows(
            adobeProgramFiles: @"C:\Program Files\Adobe",
            installDirTemplate: @"[AdobeProgramFiles]\Adobe Bridge 2026",
            stagingFolder: @"D:\dl\KBRG\pkg\1",
            installLanguage: "zh_CN");

        Assert.Equal(@"C:\Program Files\Adobe\Adobe Bridge 2026", p["INSTALLDIR"]);
        Assert.Equal(@"D:\dl\KBRG\pkg\1", p["StagingFolder"]);
    }

    [Fact]
    public void Resolve_IsCaseInsensitiveAndNested()
    {
        var p = InstallProperties.ForWindows(
            @"C:\Program Files\Adobe", @"[AdobeProgramFiles]\X", @"S", "en_US");

        // 大小写不敏感：[installdir] 与 [INSTALLDIR] 等价
        Assert.Equal(@"C:\Program Files\Adobe\X\a.exe", p.Resolve(@"[installdir]\a.exe"));
        Assert.Equal(@"S\Application", p.Resolve(@"[StagingFolder]\Application"));
    }

    [Fact]
    public void Resolve_LeavesUnknownVariablesUntouched()
    {
        var p = InstallProperties.ForWindows(@"C:\A", @"[AdobeProgramFiles]\B", @"S", "en_US");
        Assert.Equal(@"[Unknown]\x", p.Resolve(@"[Unknown]\x"));
    }

    // 回归：Illustrator 等产品的开始菜单快捷方式 Directory=[StartMenu]，必须能解析到
    // 真正的“所有用户\开始菜单\Programs”，否则快捷方式会被建到工作目录的 "[StartMenu]" 垃圾目录里。
    [Fact]
    public void ForWindows_DefinesStartMenuAndShortcutVars()
    {
        var p = InstallProperties.ForWindows(@"C:\Program Files\Adobe", @"[AdobeProgramFiles]\X", @"S", "zh_CN");

        var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
        Assert.Equal(programs, p.Resolve("[StartMenu]"));
        Assert.Equal(programs, p.Resolve("[Programs]"));
        Assert.DoesNotContain('[', p.Resolve(@"[StartMenu]\Adobe Illustrator 2026.lnk"));
    }
}

public class PimxParserTests
{
    private const string Xml = """
    <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
    <Package>
        <Type>core</Type>
        <PackageName>AdobeBridge16.0-mul-x64</PackageName>
        <ProcessorFamily>64-bit</ProcessorFamily>
        <Assets>
            <Asset source="[StagingFolder]\AMT" target="[INSTALLDIR]\AMT" recursive="true"/>
            <Asset source="[StagingFolder]\Application" target="[INSTALLDIR]" recursive="true"/>
        </Assets>
        <Commands>
            <Registry>
                <Path>HKEY_CLASSES_ROOT\.adobebridge</Path>
                <Name>Default</Name>
                <Type>REG_SZ</Type>
                <Value>Adobe.adobebridge</Value>
            </Registry>
            <Permission>
                <Path>HKEY_LOCAL_MACHINE\SOFTWARE\Adobe\Adobe Bridge\2026\Installer</Path>
                <User>Everyone</User>
                <PermissionValue>GENERIC_READ</PermissionValue>
            </Permission>
            <Shortcut>
                <Target>[INSTALLDIR]\Adobe Bridge.exe</Target>
                <Directory>[StartMenuSubFolder]</Directory>
                <Name>
                    <Language locale="en_US">Adobe Bridge 2026</Language>
                    <Language locale="zh_CN">Adobe Bridge 2026 中文</Language>
                </Name>
            </Shortcut>
            <FolderIcon>
                <FolderPath>[INSTALLDIR]</FolderPath>
                <IconPath>[INSTALLDIR]\AMT\Core files\BR_AppFolder.ico</IconPath>
            </FolderIcon>
        </Commands>
    </Package>
    """;

    [Fact]
    public void Parse_ExtractsAllCommandTypes()
    {
        var pkg = PimxParser.Parse(Xml, "zh_CN");

        Assert.Equal("AdobeBridge16.0-mul-x64", pkg.PackageName);
        Assert.Equal("core", pkg.Type);
        Assert.Equal(2, pkg.Assets.Count);
        Assert.True(pkg.Assets[0].Recursive);
        Assert.Equal(@"[StagingFolder]\AMT", pkg.Assets[0].Source);

        var reg = Assert.Single(pkg.RegistryEntries);
        Assert.Equal("Default", reg.Name);
        Assert.Equal("REG_SZ", reg.Type);

        var perm = Assert.Single(pkg.Permissions);
        Assert.Equal("Everyone", perm.User);

        var sc = Assert.Single(pkg.Shortcuts);
        Assert.Equal(@"[INSTALLDIR]\Adobe Bridge.exe", sc.Target);

        var fi = Assert.Single(pkg.FolderIcons);
        Assert.Equal("[INSTALLDIR]", fi.FolderPath);
    }

    [Fact]
    public void Parse_PicksLocalizedShortcutName()
    {
        Assert.Equal("Adobe Bridge 2026 中文", PimxParser.Parse(Xml, "zh_CN").Shortcuts[0].Name);
        Assert.Equal("Adobe Bridge 2026", PimxParser.Parse(Xml, "fr_FR").Shortcuts[0].Name); // 回退 en_US
    }

    // AEFT render engine：Directory 里用 '|' 分隔命令行参数
    private const string PipeShortcutXml = """
    <Package>
        <PackageName>X</PackageName>
        <Commands>
            <Shortcut>
                <Target>[INSTALLDIR]\App\AfterFX.exe</Target>
                <Directory>[INSTALLDIR]\App|-re</Directory>
                <Name><Language locale="en_US">Render Engine</Language></Name>
            </Shortcut>
        </Commands>
    </Package>
    """;

    [Fact]
    public void Parse_SplitsPipeArgumentsFromDirectory()
    {
        var sc = PimxParser.Parse(PipeShortcutXml, "en_US").Shortcuts[0];
        Assert.Equal(@"[INSTALLDIR]\App\AfterFX.exe", sc.Target);
        Assert.Equal(@"[INSTALLDIR]\App", sc.Directory);   // '|' 前
        Assert.Equal("-re", sc.Arguments);                 // '|' 后作为参数
    }

    // VC 运行库包：ignoreAsset + RunProgram + Condition
    private const string VcRedistXml = """
    <Package>
        <Type>core</Type>
        <PackageName>VCRedist14-64</PackageName>
        <ProcessorFamily>64-bit</ProcessorFamily>
        <Assets>
            <Asset source="[StagingFolder]" target="[InstallDir]" recursive="true" ignoreAsset="true"/>
        </Assets>
        <Commands>
            <RunProgram>
                <InstallCommand isThirdParty="true">
                    <Path>[StagingFolder]/VC_redist.x64.exe</Path>
                    <Arguments>
                        <Argument>/q</Argument>
                        <Argument>/norestart</Argument>
                    </Arguments>
                </InstallCommand>
            </RunProgram>
        </Commands>
        <Condition>[OSProcessorFamily]==64-bit</Condition>
    </Package>
    """;

    [Fact]
    public void Parse_ReadsRunProgramIgnoreAssetAndCondition()
    {
        var pkg = PimxParser.Parse(VcRedistXml, "en_US");

        Assert.Equal("[OSProcessorFamily]==64-bit", pkg.Condition);
        Assert.True(pkg.Assets[0].IgnoreAsset);

        var rp = Assert.Single(pkg.RunPrograms);
        Assert.Equal("[StagingFolder]/VC_redist.x64.exe", rp.Path);
        Assert.True(rp.IsThirdParty);
        Assert.Equal(new[] { "/q", "/norestart" }, rp.Arguments);
    }

    [Fact]
    public void ForWindows_EvaluatesOSProcessorFamilyCondition()
    {
        var p = InstallProperties.ForWindows(@"C:\A", @"[AdobeProgramFiles]\B", @"S", "en_US");
        Assert.True(p.EvaluateCondition("[OSProcessorFamily]==64-bit"));
        Assert.False(p.EvaluateCondition("[OSProcessorFamily]==32-bit"));
        Assert.True(p.EvaluateCondition(""));   // 空条件视为满足
    }
}

public class Lzma2FileDecompressorTests
{
    [Theory]
    [InlineData("Zip-Lzma2", true)]
    [InlineData("zip-lzma2", true)]
    [InlineData("", false)]
    [InlineData("none", false)]
    public void IsZipLzma2_Cases(string ct, bool expected)
        => Assert.Equal(expected, Lzma2FileDecompressor.IsZipLzma2(ct));

    [Fact]
    public void DecompressToFile_DecompressesRealLzma2File()
    {
        // 真实 pimx 也是 [dictByte][裸 LZMA2]，用它验证文件级流式解压
        var src = Path.Combine(AppContext.BaseDirectory, "TestData", "AdobeBridge16.0-mul-x64.pimx");
        var dst = Path.Combine(Path.GetTempPath(), $"lzma2test-{Guid.NewGuid():N}.xml");
        try
        {
            Lzma2FileDecompressor.DecompressToFile(src, dst);
            var text = File.ReadAllText(dst);
            Assert.StartsWith("<?xml", text.TrimStart());
            Assert.Contains("AdobeBridge16.0-mul-x64", text);
        }
        finally
        {
            if (File.Exists(dst)) File.Delete(dst);
        }
    }
}

public class DriverInfoTests
{
    private const string Xml = """
    <DriverInfo>
        <ProductInfo>
            <SapCode>KBRG</SapCode>
            <CodexVersion>16.0.4</CodexVersion>
            <BaseVersion>16.0.0</BaseVersion>
            <BuildVersion>16.0.4.40</BuildVersion>
            <EsdDirectory>KBRG</EsdDirectory>
            <Platform>win64</Platform>
            <BuildGuid>main-guid</BuildGuid>
            <Dependencies>
                <Dependency>
                    <SapCode>VC14win64</SapCode>
                    <CodexVersion>2.0.0</CodexVersion>
                    <EsdDirectory>VC14win64</EsdDirectory>
                    <Platform>win64</Platform>
                    <BuildGuid>dep-guid</BuildGuid>
                </Dependency>
            </Dependencies>
        </ProductInfo>
        <RequestInfo>
            <InstallDir>C:\Program Files\Adobe</InstallDir>
            <InstallLanguage>zh_CN</InstallLanguage>
            <TargetArchitecture>x64</TargetArchitecture>
        </RequestInfo>
    </DriverInfo>
    """;

    [Fact]
    public void Parse_ReadsProductDependenciesAndRequestInfo()
    {
        var d = DriverInfo.Parse(Xml);
        Assert.Equal("KBRG", d.Product.SapCode);
        Assert.Equal("main-guid", d.Product.BuildGuid);
        Assert.Equal(@"C:\Program Files\Adobe", d.InstallDir);
        Assert.Equal("zh_CN", d.InstallLanguage);

        var dep = Assert.Single(d.Dependencies);
        Assert.Equal("VC14win64", dep.SapCode);

        // 依赖在前，主产品在后
        var order = d.AllComponentsInInstallOrder().Select(c => c.SapCode).ToList();
        Assert.Equal(new[] { "VC14win64", "KBRG" }, order);
    }
}

public class PimxDecompressorTests
{
    [Fact]
    public void LoadXml_ReturnsPlainXmlDirectly()
    {
        var xml = "<?xml version=\"1.0\"?><Package><Type>core</Type></Package>";
        var bytes = System.Text.Encoding.UTF8.GetBytes(xml);
        Assert.Equal(xml, PimxDecompressor.LoadXml(bytes));
    }

    // 回归：真实 pimx 首字节 0x18（LZMA2 字典字节），压缩数据里也含 '<'(0x3C)。
    // 不能用「前若干字节是否含 '<'」判断明文，必须解压。
    [Fact]
    public void LoadXml_DecompressesRealLzma2Pimx()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "AdobeBridge16.0-mul-x64.pimx");
        var raw = File.ReadAllBytes(path);
        Assert.Equal(0x18, raw[0]);                        // 首字节非 '<'
        Assert.Contains((byte)'<', raw.Take(200));         // 压缩数据前段确实含 '<'

        var xml = PimxDecompressor.LoadXml(raw);
        Assert.StartsWith("<?xml", xml.TrimStart());
        Assert.Contains("<PackageName>AdobeBridge16.0-mul-x64</PackageName>", xml);

        // 解压后可被 PimxParser 正常解析
        var pkg = PimxParser.Parse(xml, "zh_CN");
        Assert.Equal("AdobeBridge16.0-mul-x64", pkg.PackageName);
        Assert.NotEmpty(pkg.Assets);
        Assert.NotEmpty(pkg.RegistryEntries);
    }
}
