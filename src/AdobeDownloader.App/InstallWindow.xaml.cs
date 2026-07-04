using System.IO;
using System.Net.Http;
using System.Windows;
using AdobeDownloader.App.Install;
using AdobeDownloader.Core;

namespace AdobeDownloader.App;

public partial class InstallWindow : Window
{
    private readonly string _driverDir;

    public InstallWindow(string driverDir)
    {
        InitializeComponent();
        _driverDir = driverDir;
        TitleText.Text = $"正在安装：{Path.GetFileName(driverDir.TrimEnd('\\'))}";
        Loaded += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        var installer = new WindowsInstaller(new AdobeApiClient(http));
        // 实时把安装日志追加到下方日志框（否则安装过程中框内空白）
        installer.Logged += line => Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
        var progress = new Progress<InstallProgress>(p =>
        {
            Bar.Value = p.Fraction * 100;
            StatusText.Text = p.Message;
        });

        try
        {
            await Task.Run(() => installer.InstallAsync(_driverDir, progress));
            StatusText.Text = "安装完成";
            Bar.Value = 100;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"安装失败：{ex.Message}";
        }
        finally
        {
            CloseButton.IsEnabled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
