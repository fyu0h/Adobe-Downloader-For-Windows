using System.Windows;
using System.Windows.Media;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.App.ViewModels;

/// <summary>
/// 按 SapCode 归组的产品：同一产品的多个版本聚合在一起，供 UI 展示与版本选择。
/// </summary>
public sealed class ProductGroupViewModel : ObservableObject
{
    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<Product> Versions { get; }

    public ProductGroupViewModel(string id, string displayName, IEnumerable<Product> versions)
    {
        Id = id;
        DisplayName = displayName;
        Versions = versions
            .OrderByDescending(v => v.Version, VersionComparer.Instance)
            .ToList();

        _ = LoadIconAsync();
    }

    public string? IconUrl => Versions
        .Select(v => v.GetBestIcon()?.Value)
        .FirstOrDefault(u => !string.IsNullOrEmpty(u));

    private ImageSource? _icon;
    /// <summary>产品图标，走本地磁盘缓存异步加载，避免每次启动联网重复获取。</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set => SetField(ref _icon, value);
    }

    private async Task LoadIconAsync()
    {
        var img = await IconCache.LoadAsync(IconUrl);
        if (img is not null)
            Application.Current?.Dispatcher.Invoke(() => Icon = img);
    }

    public string DisplayLabel => $"{DisplayName}  ({Id})";

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();
        public int Compare(string? x, string? y)
        {
            var a = (x ?? "").Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var b = (y ?? "").Split('.').Select(s => int.TryParse(s, out var n) ? n : 0).ToArray();
            var len = Math.Max(a.Length, b.Length);
            for (var i = 0; i < len; i++)
            {
                var p = i < a.Length ? a[i] : 0;
                var q = i < b.Length ? b[i] : 0;
                if (p != q) return p - q;
            }
            return 0;
        }
    }
}
