using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using AdobeDownloader.App.Cleanup;
using AdobeDownloader.Core.Cleanup;

namespace AdobeDownloader.App;

public partial class CleanupWindow : Window
{
    private readonly ObservableCollection<CleanupOptionItem> _options = new();
    private List<CleanupOption> _lastScannedSelection = new();

    public CleanupWindow()
    {
        InitializeComponent();
        foreach (var o in CleanupOptionExtensions.ExecutionOrder)
            _options.Add(new CleanupOptionItem(o, selected: true));
        OptionsList.ItemsSource = _options;
    }

    private async void OnScan(object sender, RoutedEventArgs e)
    {
        var selected = _options.Where(x => x.IsSelected).Select(x => x.Option).ToList();
        if (selected.Count == 0)
        {
            StatusText.Text = "请至少勾选一个类别";
            return;
        }

        ScanButton.IsEnabled = false;
        CleanButton.IsEnabled = false;
        StatusText.Text = "正在扫描...";
        try
        {
            var progress = new Progress<string>(s => StatusText.Text = s);
            var plan = await Task.Run(() => new CleanupPlanner().BuildPlan(selected, progress));
            PreviewList.ItemsSource = plan.Items;
            var mb = plan.EstimatedBytes / 1024.0 / 1024.0;
            PreviewSummary.Text = $"将清理 {plan.Items.Count} 项，预计释放约 {mb:F1} MB";
            StatusText.Text = plan.Items.Count == 0 ? "未发现可清理的内容" : "扫描完成，确认后点击“开始清理”";
            _lastScannedSelection = selected;
            CleanButton.IsEnabled = plan.Items.Count > 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            ScanButton.IsEnabled = true;
        }
    }

    private void OnClean(object sender, RoutedEventArgs e)
    {
        if (_lastScannedSelection.Count == 0) return;

        var result = AppDialog.Show(
            "确定要开始清理吗？此操作会永久删除上述 Adobe 相关文件、注册表项、服务与 hosts 条目，无法撤销。\n\n" +
            "将请求管理员权限。",
            "确认清理", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (result != MessageBoxResult.OK) return;

        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定自身可执行文件路径");
            var arg = string.Join(",", _lastScannedSelection.Select(o => o.ToString()));
            Process.Start(new ProcessStartInfo(exe, $"--cleanup {arg}")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"启动清理失败：{ex.Message}";
        }
    }
}
