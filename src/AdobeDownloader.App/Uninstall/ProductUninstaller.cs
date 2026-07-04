using System.IO;
using AdobeDownloader.App.Install;
using AdobeDownloader.Core.Install;

namespace AdobeDownloader.App.Uninstall;

/// <summary>
/// 卸载由本引擎安装的 Adobe 产品：按安装记录回删快捷方式、安装目录、ARP 注册表项。
/// 只删记录里的东西；不逐条回删 pimx 写入的大量注册表（那部分交给“🧹 清理”功能）。
/// </summary>
public sealed class ProductUninstaller
{
    public event Action<string>? Logged;

    private void Info(string message) => Logged?.Invoke(message);

    /// <summary>执行卸载。recordPath 为安装记录 JSON 路径。</summary>
    public void Uninstall(InstallRecord record, IProgress<double>? progress = null)
    {
        Info($"开始卸载：{record.DisplayName}");

        // 1) 删除快捷方式
        foreach (var lnk in record.Shortcuts)
        {
            try
            {
                if (File.Exists(lnk)) { File.Delete(lnk); Info($"删除快捷方式：{lnk}"); }
            }
            catch (Exception ex) { Info($"[警告] 删除快捷方式失败 {lnk}：{ex.Message}"); }
        }
        progress?.Report(0.2);

        // 2) 删除安装目录（含清只读/系统属性；带路径安全校验）
        DeleteInstallDir(record.InstallLocation, progress);

        // 3) 删除 ARP 注册表项与安装记录 JSON
        try
        {
            InstallRegistry.Remove(record);
            Info("已从系统卸载列表移除");
        }
        catch (Exception ex) { Info($"[警告] 移除卸载注册表项失败：{ex.Message}"); }

        progress?.Report(1.0);
        Info($"卸载完成：{record.DisplayName}");
    }

    /// <summary>
    /// 强制删除（无安装记录/厂商卸载器不可用时）：删安装目录、指向它的快捷方式、匹配的残留卸载注册表项。
    /// 只需安装目录，其余在此进程内自行发现。
    /// </summary>
    public void ForceRemove(string installDir, IProgress<double>? progress = null)
    {
        var dir = installDir.TrimEnd('\\', '/');
        var name = Path.GetFileName(dir);
        Info($"强制删除：{name}");

        // 1) 删除指向该目录的快捷方式
        foreach (var lnk in ShortcutFinder.FindTargeting(dir))
        {
            try { if (File.Exists(lnk)) { File.Delete(lnk); Info($"删除快捷方式：{lnk}"); } }
            catch (Exception ex) { Info($"[警告] 删除快捷方式失败 {lnk}：{ex.Message}"); }
        }
        progress?.Report(0.2);

        // 2) 删除安装目录
        DeleteInstallDir(dir, progress);

        // 3) 删除匹配的残留卸载注册表项
        try
        {
            var removed = InstallRegistry.RemoveArpEntriesFor(dir, name);
            foreach (var k in removed) Info($"删除残留卸载项：{k}");
        }
        catch (Exception ex) { Info($"[警告] 清理卸载注册表项失败：{ex.Message}"); }

        progress?.Report(1.0);
        Info($"强制删除完成：{name}");
    }

    private void DeleteInstallDir(string dir, IProgress<double>? progress)
    {
        if (!IsSafeInstallDir(dir))
        {
            Info($"[警告] 安装目录不安全或不存在，跳过删除：{dir}");
            return;
        }

        // FolderIcon 会把目录设只读、desktop.ini 设隐藏/系统，先清属性以便删除
        try
        {
            foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var a = File.GetAttributes(f);
                    if ((a & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                        File.SetAttributes(f, FileAttributes.Normal);
                }
                catch { /* 跳过单个文件 */ }
            }
        }
        catch { /* 枚举失败也继续尝试删 */ }

        progress?.Report(0.6);

        try
        {
            Directory.Delete(dir, recursive: true);
            Info($"删除安装目录：{dir}");
        }
        catch (Exception ex)
        {
            Info($"[警告] 删除安装目录失败：{ex.Message}");
        }
    }

    /// <summary>安全校验：必须是已存在的、足够深的、路径含 Adobe 的目录，避免误删。</summary>
    private static bool IsSafeInstallDir(string dir)
    {
        if (string.IsNullOrWhiteSpace(dir)) return false;
        var full = dir.TrimEnd('\\', '/');
        if (full.Length <= 3) return false;                       // 非盘根
        if (!Directory.Exists(full)) return false;
        if (full.Contains('[')) return false;                     // 未解析变量
        if (!full.Contains("Adobe", StringComparison.OrdinalIgnoreCase)) return false;
        // 至少两级子目录（如 C:\Program Files\Adobe\Adobe Illustrator 2026）
        return full.Count(c => c is '\\' or '/') >= 2;
    }
}
