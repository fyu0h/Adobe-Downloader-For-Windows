using System.IO;

namespace AdobeDownloader.App.Uninstall;

/// <summary>在开始菜单/桌面里找出目标指向某安装目录的快捷方式（.lnk），供强制删除时清理。</summary>
public static class ShortcutFinder
{
    /// <summary>返回目标(TargetPath)位于 installDir 之下的所有 .lnk 全路径。</summary>
    public static List<string> FindTargeting(string installDir)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(installDir)) return result;
        var root = installDir.TrimEnd('\\', '/');

        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };

        dynamic? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is not null) shell = Activator.CreateInstance(shellType);
            if (shell is null) return result;

            foreach (var dir in dirs.Distinct())
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
                foreach (var lnk in SafeEnumerateLnk(dir))
                {
                    try
                    {
                        dynamic sc = shell.CreateShortcut(lnk);
                        string target = sc.TargetPath ?? "";
                        if (!string.IsNullOrEmpty(target) &&
                            target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            result.Add(lnk);
                    }
                    catch { /* 跳过单个 */ }
                }
            }
        }
        catch { /* WScript.Shell 不可用 */ }

        return result;
    }

    private static IEnumerable<string> SafeEnumerateLnk(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*.lnk", SearchOption.AllDirectories); }
        catch { return Enumerable.Empty<string>(); }
    }
}
