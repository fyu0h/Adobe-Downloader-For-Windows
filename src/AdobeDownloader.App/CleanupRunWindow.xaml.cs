using System.Windows;
using AdobeDownloader.App.Cleanup;
using AdobeDownloader.Core.Cleanup;

namespace AdobeDownloader.App;

public partial class CleanupRunWindow : Window
{
    private readonly List<CleanupOption> _options;

    public CleanupRunWindow(List<CleanupOption> options)
    {
        InitializeComponent();
        _options = options;
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var executor = new CleanupExecutor();
        try
        {
            StatusText.Text = "正在扫描...";
            var scanProgress = new Progress<string>(s => StatusText.Text = s);
            var plan = await Task.Run(() => new CleanupPlanner().BuildPlan(_options, scanProgress));

            if (plan.Items.Count == 0)
            {
                StatusText.Text = "未发现可清理的内容";
                Bar.Value = 100;
                return;
            }

            var progress = new Progress<CleanupProgress>(p =>
            {
                Bar.Value = p.Fraction * 100;
                StatusText.Text = p.Message;
            });
            await executor.ExecuteAsync(plan, progress);

            var mb = executor.RemovedBytes / 1024.0 / 1024.0;
            StatusText.Text = $"清理完成：处理 {executor.RemovedCount} 项，释放约 {mb:F1} MB";
            Bar.Value = 100;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"清理失败：{ex.Message}";
        }
        finally
        {
            LogBox.Text = string.Join(Environment.NewLine, executor.Log);
            LogBox.ScrollToEnd();
            CloseButton.IsEnabled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
