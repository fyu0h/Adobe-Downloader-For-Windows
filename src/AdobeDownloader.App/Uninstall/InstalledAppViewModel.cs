using System.Windows;
using System.Windows.Media;
using AdobeDownloader.App.ViewModels;

namespace AdobeDownloader.App.Uninstall;

/// <summary>卸载列表中的一行：包裹 InstalledApp，异步提取其程序图标。</summary>
public sealed class InstalledAppViewModel : ObservableObject
{
    public InstalledApp Model { get; }

    public InstalledAppViewModel(InstalledApp model)
    {
        Model = model;
        _ = LoadIconAsync();
    }

    public string DisplayName => Model.DisplayName;
    public string Version => string.IsNullOrWhiteSpace(Model.Version) ? "" : $"版本 {Model.Version}";
    public bool CanUninstall => Model.CanUninstall;
    public bool CanForceRemove => Model.CanForceRemove;

    public string SizeText => Model.EstimatedSizeBytes > 0
        ? $"{Model.EstimatedSizeBytes / 1024.0 / 1024.0:F0} MB"
        : "";

    private ImageSource? _icon;
    public ImageSource? Icon
    {
        get => _icon;
        private set => SetField(ref _icon, value);
    }

    private async Task LoadIconAsync()
    {
        var img = await Task.Run(() => AppIconExtractor.Extract(Model.IconSource));
        if (img is not null)
            Application.Current?.Dispatcher.Invoke(() => Icon = img);
    }
}
