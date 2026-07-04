using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AdobeDownloader.App;

/// <summary>
/// 与程序风格统一的消息/确认对话框，替代系统原生 MessageBox。
/// 用法与 MessageBox.Show 类似，返回 MessageBoxResult。
/// </summary>
public partial class AppDialog : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private AppDialog()
    {
        InitializeComponent();
        // 支持按住标题区域拖动窗口
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    /// <summary>显示对话框。owner 为空时自动取当前活动窗口，居中于其上。</summary>
    public static MessageBoxResult Show(
        string message,
        string title = "Adobe Downloader",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None,
        Window? owner = null)
    {
        var dlg = new AppDialog();
        dlg.TitleText.Text = title;
        dlg.MessageText.Text = message;
        dlg.ApplyIcon(image);
        dlg.BuildButtons(buttons);

        owner ??= ActiveOwner();
        if (owner is not null && !ReferenceEquals(owner, dlg))
        {
            dlg.Owner = owner;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else
        {
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        dlg.ShowDialog();
        return dlg._result;
    }

    private static Window? ActiveOwner()
    {
        if (Application.Current is null) return null;
        Window? active = null;
        foreach (Window w in Application.Current.Windows)
        {
            if (w is AppDialog) continue;
            if (w.IsActive) { active = w; break; }
            active ??= w;
        }
        return active;
    }

    private void ApplyIcon(MessageBoxImage image)
    {
        var (glyph, color) = image switch
        {
            MessageBoxImage.Error => ("✕", "#E4002B"),
            MessageBoxImage.Warning => ("!", "#E8912D"),
            MessageBoxImage.Question => ("?", "#2D7FF0"),
            MessageBoxImage.Information => ("i", "#2D7FF0"),
            _ => ("", ""),
        };
        if (glyph.Length == 0) return;

        IconGlyph.Text = glyph;
        IconBadge.Background = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(color));
        IconBadge.Visibility = Visibility.Visible;
    }

    private void BuildButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("确定", MessageBoxResult.OK, primary: true, isDefault: true, isCancel: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("取消", MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                AddButton("确定", MessageBoxResult.OK, primary: true, isDefault: true, isCancel: false);
                break;
            case MessageBoxButton.YesNo:
                AddButton("否", MessageBoxResult.No, primary: false, isDefault: false, isCancel: true);
                AddButton("是", MessageBoxResult.Yes, primary: true, isDefault: true, isCancel: false);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("取消", MessageBoxResult.Cancel, primary: false, isDefault: false, isCancel: true);
                AddButton("否", MessageBoxResult.No, primary: false, isDefault: false, isCancel: false);
                AddButton("是", MessageBoxResult.Yes, primary: true, isDefault: true, isCancel: false);
                break;
        }
    }

    private void AddButton(string text, MessageBoxResult result, bool primary, bool isDefault, bool isCancel)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 84,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(16, 7, 16, 7),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        if (primary && TryFindResource("PrimaryButton") is Style s)
            btn.Style = s;

        btn.Click += (_, _) => { _result = result; Close(); };
        ButtonPanel.Children.Add(btn);
    }
}
