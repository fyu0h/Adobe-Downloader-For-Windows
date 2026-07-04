using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AdobeDownloader.App.Uninstall;

/// <summary>
/// 从注册表 DisplayIcon（"路径,索引" 形式，指向 exe 或 ico）提取程序图标为 WPF 位图。
/// 用 shell32.ExtractIconEx 取图标句柄，避免引入 System.Drawing 依赖。
/// </summary>
public static class AppIconExtractor
{
    /// <summary>从 DisplayIcon 值提取图标；失败返回 null。返回的位图已 Freeze。</summary>
    public static BitmapSource? Extract(string? displayIcon)
    {
        if (string.IsNullOrWhiteSpace(displayIcon)) return null;

        var (path, index) = ParseIconSource(displayIcon);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var hIcon = IntPtr.Zero;
        try
        {
            var large = new IntPtr[1];
            var small = new IntPtr[1];
            var count = ExtractIconEx(path, index, large, small, 1);
            hIcon = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (count <= 0 || hIcon == IntPtr.Zero) return null;

            var bmp = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            bmp.Freeze();
            // 释放两个句柄
            if (small[0] != IntPtr.Zero && small[0] != hIcon) DestroyIcon(small[0]);
            if (large[0] != IntPtr.Zero && large[0] != hIcon) DestroyIcon(large[0]);
            return bmp;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        }
    }

    /// <summary>拆分 "C:\path\app.exe,0" → (路径, 索引)。索引缺省为 0。</summary>
    private static (string path, int index) ParseIconSource(string src)
    {
        src = src.Trim().Trim('"');
        var comma = src.LastIndexOf(',');
        // 仅当逗号后是纯数字才当作索引（避免误切路径中的逗号）
        if (comma > 1 && int.TryParse(src[(comma + 1)..].Trim(), out var idx))
            return (src[..comma].Trim().Trim('"'), idx);
        return (src, 0);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(
        string lpszFile, int nIconIndex, IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
