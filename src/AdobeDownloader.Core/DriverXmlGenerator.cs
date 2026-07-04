using System.Security;
using System.Text;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.Core;

/// <summary>
/// 生成 Adobe HyperDrive 安装器所需的 driver.xml（对应原版 generateDriverXML），
/// 针对 Windows 平台：InstallDir 使用 Windows 路径，Platform 使用 win64/winarm64。
/// </summary>
public static class DriverXmlGenerator
{
    /// <summary>Adobe 在 Windows 上的默认安装根目录。</summary>
    public const string DefaultWindowsInstallDir = @"C:\Program Files\Adobe";

    public static string Generate(DownloadPlan plan, string installDir = DefaultWindowsInstallDir)
    {
        var main = plan.Components.FirstOrDefault(c => c.IsMainProduct)
                   ?? plan.Components.FirstOrDefault();
        if (main is null) return "";

        var platform = string.IsNullOrEmpty(main.Platform) ? plan.Platform : main.Platform;
        var buildVersion = string.IsNullOrEmpty(main.BuildVersion) ? plan.ProductVersion : main.BuildVersion;
        var baseVersion = string.IsNullOrEmpty(main.BaseVersion) ? plan.ProductVersion : main.BaseVersion;

        var sb = new StringBuilder();
        var depsXml = new StringBuilder();
        foreach (var dep in plan.Components.Where(c => !c.IsMainProduct))
        {
            var depBuild = string.IsNullOrEmpty(dep.BuildVersion) ? dep.Version : dep.BuildVersion;
            var depBase = string.IsNullOrEmpty(dep.BaseVersion) ? dep.Version : dep.BaseVersion;
            depsXml.Append($"""
                    <Dependency>
                        <SapCode>{E(dep.SapCode)}</SapCode>
                        <CodexVersion>{E(dep.Version)}</CodexVersion>
                        <BaseVersion>{E(depBase)}</BaseVersion>
                        <BuildVersion>{E(depBuild)}</BuildVersion>
                        <EsdDirectory>{E(dep.SapCode)}</EsdDirectory>
                        <Platform>{E(dep.Platform)}</Platform>
                        <BuildGuid>{E(dep.BuildGuid)}</BuildGuid>
                    </Dependency>

            """);
        }

        sb.Append($"""
        <DriverInfo>
            <ProductInfo>
                <SapCode>{E(main.SapCode)}</SapCode>
                <CodexVersion>{E(plan.ProductVersion)}</CodexVersion>
                <BaseVersion>{E(baseVersion)}</BaseVersion>
                <BuildVersion>{E(buildVersion)}</BuildVersion>
                <EsdDirectory>{E(main.SapCode)}</EsdDirectory>
                <Platform>{E(platform)}</Platform>
                <BuildGuid>{E(main.BuildGuid)}</BuildGuid>
                <Dependencies>
        {depsXml}        </Dependencies>
            </ProductInfo>
            <RequestInfo>
                <InstallDir>{E(installDir)}</InstallDir>
                <InstallLanguage>{E(plan.Language)}</InstallLanguage>
                <TargetArchitecture>{E(plan.Architecture.DriverValue())}</TargetArchitecture>
            </RequestInfo>
        </DriverInfo>
        """);

        return sb.ToString();
    }

    private static string E(string s) => SecurityElement.Escape(s) ?? "";
}
