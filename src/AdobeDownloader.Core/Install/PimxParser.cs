using System.Xml.Linq;

namespace AdobeDownloader.Core.Install;

/// <summary>
/// 解析 Windows 版 pimx 清单 XML 为命令模型。对应原版 PIMXParser.parse，但针对
/// Windows 的 Assets/Registry/Permission/Shortcut/FolderIcon 命令。
/// </summary>
public static class PimxParser
{
    public static PimxPackage Parse(string xml, string installLanguage)
    {
        var root = XDocument.Parse(xml).Root
            ?? throw new FormatException("pimx 根节点缺失");

        var pkg = new PimxPackage
        {
            PackageName = root.Element("PackageName")?.Value.Trim() ?? "",
            Type = root.Element("Type")?.Value.Trim() ?? "",
            ProcessorFamily = root.Element("ProcessorFamily")?.Value.Trim() ?? "",
            Condition = root.Element("Condition")?.Value.Trim() ?? "",
        };

        var lang = FirstLangToken(installLanguage);

        foreach (var a in root.Element("Assets")?.Elements("Asset") ?? Enumerable.Empty<XElement>())
        {
            pkg.Assets.Add(new PimxAsset
            {
                Source = (string?)a.Attribute("source") ?? "",
                Target = (string?)a.Attribute("target") ?? "",
                Recursive = string.Equals((string?)a.Attribute("recursive"), "true", StringComparison.OrdinalIgnoreCase),
                IgnoreAsset = string.Equals((string?)a.Attribute("ignoreAsset"), "true", StringComparison.OrdinalIgnoreCase),
            });
        }

        var commands = root.Element("Commands");
        if (commands is not null)
        {
            foreach (var r in commands.Elements("Registry"))
            {
                pkg.RegistryEntries.Add(new PimxRegistry
                {
                    Path = r.Element("Path")?.Value.Trim() ?? "",
                    Name = LocalizedText(r.Element("Name"), lang),
                    Type = r.Element("Type")?.Value.Trim() ?? "REG_SZ",
                    Value = LocalizedText(r.Element("Value"), lang),
                });
            }

            foreach (var p in commands.Elements("Permission"))
            {
                pkg.Permissions.Add(new PimxPermission
                {
                    Path = p.Element("Path")?.Value.Trim() ?? "",
                    User = p.Element("User")?.Value.Trim() ?? "",
                    PermissionValue = p.Element("PermissionValue")?.Value.Trim() ?? "",
                });
            }

            foreach (var s in commands.Elements("Shortcut"))
            {
                // Adobe 在 Target/Directory 里用 '|' 分隔命令行参数（如 ...|-re 表示 render engine 参数）
                var (target, targetArgs) = SplitPipe(s.Element("Target")?.Value.Trim() ?? "");
                var (directory, dirArgs) = SplitPipe(s.Element("Directory")?.Value.Trim() ?? "");
                var args = string.Join(" ", new[]
                {
                    s.Element("Arguments")?.Value.Trim() ?? "", targetArgs, dirArgs
                }.Where(a => !string.IsNullOrEmpty(a)));

                pkg.Shortcuts.Add(new PimxShortcut
                {
                    Target = target,
                    Directory = directory,
                    Name = LocalizedText(s.Element("Name"), lang),
                    Arguments = args,
                    WorkingDirectory = s.Element("WorkingDirectory")?.Value.Trim() ?? "",
                    IconPath = s.Element("IconPath")?.Value.Trim() ?? "",
                });
            }

            foreach (var f in commands.Elements("FolderIcon"))
            {
                pkg.FolderIcons.Add(new PimxFolderIcon
                {
                    FolderPath = f.Element("FolderPath")?.Value.Trim() ?? "",
                    IconPath = f.Element("IconPath")?.Value.Trim() ?? "",
                });
            }

            // RunProgram：取 InstallCommand（含 Path 与 Arguments），如 VC 运行库安装器
            foreach (var rp in commands.Elements("RunProgram"))
            {
                var cmd = rp.Element("InstallCommand");
                if (cmd is null) continue;
                var path = cmd.Element("Path")?.Value.Trim() ?? "";
                if (string.IsNullOrEmpty(path)) continue;

                pkg.RunPrograms.Add(new PimxRunProgram
                {
                    Path = path,
                    IsThirdParty = string.Equals((string?)cmd.Attribute("isThirdParty"), "true", StringComparison.OrdinalIgnoreCase),
                    Arguments = cmd.Element("Arguments")?.Elements("Argument")
                        .Select(x => x.Value.Trim()).Where(x => x.Length > 0).ToList() ?? new List<string>(),
                });
            }
        }

        return pkg;
    }

    /// <summary>
    /// 取文本：若元素含 &lt;Language locale="xx"&gt; 子节点，按语言选（回退 en_US、再回退首个）；
    /// 否则取元素文本。
    /// </summary>
    private static string LocalizedText(XElement? element, string lang)
    {
        if (element is null) return "";

        var langs = element.Elements("Language").ToList();
        if (langs.Count == 0)
            return element.Value.Trim();

        string? Pick(string locale) => langs
            .FirstOrDefault(l => string.Equals((string?)l.Attribute("locale"), locale, StringComparison.OrdinalIgnoreCase))
            ?.Value.Trim();

        return Pick(lang) ?? Pick("en_US") ?? langs[0].Value.Trim();
    }

    /// <summary>拆分 "路径|参数"：返回 (路径, 参数)。无 '|' 时参数为空。</summary>
    private static (string path, string args) SplitPipe(string value)
    {
        var idx = value.IndexOf('|');
        return idx < 0 ? (value, "") : (value[..idx].Trim(), value[(idx + 1)..].Trim());
    }

    private static string FirstLangToken(string installLanguage)
    {
        var token = installLanguage.Split(',').FirstOrDefault()?.Trim() ?? installLanguage;
        return string.IsNullOrEmpty(token) || token == "ALL" ? "en_US" : token;
    }
}
