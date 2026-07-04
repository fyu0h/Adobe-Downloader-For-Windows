using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;

namespace AdobeDownloader.App;

/// <summary>
/// 产品图标本地磁盘缓存。首次按 URL 下载并存盘，之后各次启动直接从磁盘加载，
/// 不再联网重复获取。返回的 BitmapImage 已 Freeze，可跨线程绑定。
/// </summary>
public static class IconCache
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AdobeDownloader", "icons");

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>加载图标：命中磁盘缓存直接读盘，否则下载一次并写盘。失败返回 null。</summary>
    public static async Task<BitmapImage?> LoadAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        try
        {
            var file = Path.Combine(Dir, Hash(url) + ".img");
            byte[] bytes;
            if (File.Exists(file))
            {
                bytes = await File.ReadAllBytesAsync(file);
            }
            else
            {
                bytes = await Http.GetByteArrayAsync(url);
                Directory.CreateDirectory(Dir);
                await File.WriteAllBytesAsync(file, bytes);
            }
            return FromBytes(bytes);
        }
        catch
        {
            return null;
        }
    }

    private static BitmapImage FromBytes(byte[] bytes)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.StreamSource = new MemoryStream(bytes);
        bmp.DecodePixelWidth = 48;                     // 图标只需小尺寸，省内存
        bmp.CacheOption = BitmapCacheOption.OnLoad;    // 一次性读入，不锁定文件
        bmp.EndInit();
        bmp.Freeze();                                  // 冻结后可跨线程使用
        return bmp;
    }

    private static string Hash(string s)
        => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)));
}
