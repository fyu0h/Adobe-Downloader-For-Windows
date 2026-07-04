using System.Text.RegularExpressions;

namespace AdobeDownloader.Core.Install;

/// <summary>
/// 安装变量表：把 pimx 里的 [INSTALLDIR]/[StagingFolder]/[AdobeCommon] 等占位符替换成实际路径。
/// 对应原版 HDPIMPropertyTable，但用 Windows 目录约定。变量名大小写不敏感，支持嵌套展开。
/// </summary>
public sealed partial class InstallProperties
{
    private readonly Dictionary<string, string> _vars = new(StringComparer.OrdinalIgnoreCase);

    public InstallProperties() { }

    public string this[string name]
    {
        get => _vars.TryGetValue(name, out var v) ? v : "";
        set => _vars[name] = value;
    }

    /// <summary>
    /// 构造 Windows 安装变量。
    /// </summary>
    /// <param name="adobeProgramFiles">driver.xml 的 InstallDir，如 C:\Program Files\Adobe</param>
    /// <param name="installDirTemplate">application.json 的 InstallDir.value，如 [AdobeProgramFiles]\Adobe Bridge 2026</param>
    /// <param name="stagingFolder">包解压目录</param>
    /// <param name="installLanguage">安装语言，如 zh_CN</param>
    /// <param name="adobeCode">AMT 产品代码（可空）</param>
    public static InstallProperties ForWindows(
        string adobeProgramFiles, string installDirTemplate, string stagingFolder,
        string installLanguage, string adobeCode = "", string architecture = "x64")
    {
        var commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        var commonStartMenu = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);

        var p = new InstallProperties
        {
            ["AdobeProgramFiles"] = adobeProgramFiles,
            ["CommonProgramFiles"] = commonProgramFiles,
            ["ProgramFiles"] = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ["AdobeCommon"] = Path.Combine(commonProgramFiles, "Adobe"),
            ["StartMenuSubFolder"] = Path.Combine(commonStartMenu, "Programs"),
            // pimx 快捷方式常用 [StartMenu]/[Programs]/[Desktop] 作目标目录（全部用户）
            ["StartMenu"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            ["Programs"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            ["CommonPrograms"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            ["Desktop"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            ["DesktopFolder"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            ["StagingFolder"] = stagingFolder,
            ["installLanguage"] = installLanguage,
            ["AdobeCode"] = adobeCode,
            // 公共/用户目录（AEFT 等重型产品会用到）
            ["SharedDocuments"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            ["UserRoamingAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["UserLocalAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ["CommonAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["SystemRoot"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            ["UserProfile"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["CommonDesktop"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            // 供包级条件（如 VC 运行库的 [OSProcessorFamily]==64-bit）与路径解析用
            ["OSProcessorFamily"] = "64-bit",
            ["OSArchitecture"] = string.Equals(architecture, "arm64", StringComparison.OrdinalIgnoreCase) ? "arm64" : "x64",
        };
        // INSTALLDIR 依赖 AdobeProgramFiles，先展开
        p["INSTALLDIR"] = p.Resolve(installDirTemplate);
        return p;
    }

    /// <summary>用当前变量求值包级条件（如 [OSProcessorFamily]==64-bit）；空条件视为满足。</summary>
    public bool EvaluateCondition(string condition)
        => Selection.ConditionEvaluator.Evaluate(condition, _vars);

    /// <summary>把模板里所有 [Var] 替换为值，最多展开若干层以处理嵌套。</summary>
    public string Resolve(string template)
    {
        if (string.IsNullOrEmpty(template)) return template ?? "";
        var current = template;
        for (var i = 0; i < 8; i++)
        {
            var replaced = VarRegex().Replace(current, m =>
            {
                var name = m.Groups[1].Value;
                return _vars.TryGetValue(name, out var v) ? v : m.Value;
            });
            if (replaced == current) break;
            current = replaced;
        }
        return current;
    }

    [GeneratedRegex(@"\[([A-Za-z][A-Za-z0-9_]*)\]")]
    private static partial Regex VarRegex();
}
