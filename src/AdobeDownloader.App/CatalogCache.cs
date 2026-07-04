using System.IO;
using AdobeDownloader.Core;

namespace AdobeDownloader.App;

/// <summary>
/// 把产品目录原始 XML 缓存到本地，按架构区分。
/// 启动时直接读缓存，避免每次打开都联网“刷新目录”；用户手动刷新时更新缓存。
/// </summary>
public static class CatalogCache
{
    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdobeDownloader");

    private static string FilePath(TargetArchitecture arch)
        => Path.Combine(Dir, $"catalog-{arch}.xml");

    /// <summary>读取指定架构的缓存 XML 及其保存时间；无缓存返回 null。</summary>
    public static (string Xml, DateTime SavedAt)? Load(TargetArchitecture arch)
    {
        try
        {
            var file = FilePath(arch);
            if (!File.Exists(file)) return null;
            return (File.ReadAllText(file), File.GetLastWriteTime(file));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>保存指定架构的目录 XML 到本地缓存。</summary>
    public static void Save(TargetArchitecture arch, string xml)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath(arch), xml);
        }
        catch
        {
            /* 缓存失败不影响主流程 */
        }
    }
}
