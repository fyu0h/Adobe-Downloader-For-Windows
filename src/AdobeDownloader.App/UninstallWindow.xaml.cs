using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AdobeDownloader.App.Uninstall;

namespace AdobeDownloader.App;

/// <summary>
/// 卸载窗口：列出本机已安装的 Adobe 程序（带各自图标），可单独卸载某个版本。
/// 卸载委托该程序注册表里的卸载命令执行，以管理员权限运行。
/// </summary>
public partial class UninstallWindow : Window
{
    private readonly ObservableCollection<InstalledAppViewModel> _apps = new();

    public UninstallWindow()
    {
        InitializeComponent();
        AppList.ItemsSource = _apps;
        Loaded += (_, _) => ShowStoredResults();
    }

    /// <summary>展示已存储的扫描结果（启动时后台扫描的结果）。</summary>
    private void ShowStoredResults()
    {
        _apps.Clear();
        foreach (var app in InstalledAppsStore.Current)
            _apps.Add(new InstalledAppViewModel(app));

        EmptyHint.Visibility = _apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = InstalledAppsStore.ScannedAt is { } t
            ? $"共 {_apps.Count} 个程序（{t:yyyy-MM-dd HH:mm} 扫描）"
            : $"共 {_apps.Count} 个程序";
    }

    private async void OnRescan(object sender, RoutedEventArgs e)
    {
        RescanButton.IsEnabled = false;
        StatusText.Text = "正在扫描已安装程序...";
        try
        {
            var apps = await Task.Run(InstalledAppScanner.Scan);
            InstalledAppsStore.Save(apps);
            ShowStoredResults();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"扫描失败：{ex.Message}";
        }
        finally
        {
            RescanButton.IsEnabled = true;
        }
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledAppViewModel vm }) return;
        var app = vm.Model;
        if (!app.CanUninstall)
        {
            AppDialog.Show("该程序未提供卸载命令，无法卸载。", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = AppDialog.Show(
            $"确定要卸载 “{app.DisplayName}” 吗？\n\n将调用该程序自带的卸载器并请求管理员权限。",
            "确认卸载", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var (exe, args) = SplitCommand(app.EffectiveUninstallCommand);
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                Verb = "runas", // 请求管理员权限
            });
            StatusText.Text = $"已启动 “{app.DisplayName}” 的卸载器，完成后可点“重新扫描”刷新列表";
        }
        catch (Exception ex)
        {
            AppDialog.Show($"启动卸载失败：{ex.Message}", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnForceRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: InstalledAppViewModel vm }) return;
        var app = vm.Model;
        if (!app.CanForceRemove) return;

        var confirm = AppDialog.Show(
            $"强制删除 “{app.DisplayName}” ？\n\n将直接删除安装目录、指向它的快捷方式，以及残留的卸载注册表项：\n{app.InstallLocation}\n\n" +
            "适用于厂商卸载器失效或列表里没有卸载项的情况。此操作无法撤销，需要管理员权限。",
            "强制删除", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.OK) return;

        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定自身可执行文件路径");
            Process.Start(new ProcessStartInfo(exe, $"--forceremove \"{app.InstallLocation}\"")
            {
                UseShellExecute = true,
                Verb = "runas",
            });
            StatusText.Text = $"已启动强制删除 “{app.DisplayName}”，完成后点“重新扫描”刷新列表";
        }
        catch (Exception ex)
        {
            AppDialog.Show($"启动强制删除失败：{ex.Message}", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>把注册表卸载命令拆成可执行文件与参数，供 ShellExecute 提权运行。</summary>
    internal static (string exe, string args) SplitCommand(string command)
    {
        var cmd = command.Trim();
        if (cmd.StartsWith('"'))
        {
            var end = cmd.IndexOf('"', 1);
            if (end > 0)
                return (cmd.Substring(1, end - 1), cmd[(end + 1)..].Trim());
        }

        // 未加引号：优先在 ".exe" 处切分（路径可能含空格）
        var idx = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (idx > 0)
        {
            var cut = idx + 4;
            return (cmd[..cut], cmd[cut..].Trim());
        }

        var sp = cmd.IndexOf(' ');
        return sp < 0 ? (cmd, "") : (cmd[..sp], cmd[(sp + 1)..].Trim());
    }
}
