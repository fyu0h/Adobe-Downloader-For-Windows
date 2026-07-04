using AdobeDownloader.Core.Cleanup;

namespace AdobeDownloader.App.Cleanup;

/// <summary>清理类别在 UI 上的可勾选项。</summary>
public sealed class CleanupOptionItem : ObservableObject
{
    public CleanupOption Option { get; }
    public string Name => Option.DisplayName();
    public string Description => Option.Description();

    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

    public CleanupOptionItem(CleanupOption option, bool selected)
    {
        Option = option;
        _isSelected = selected;
    }
}
