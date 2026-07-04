using System.IO;
using System.Windows;
using AdobeDownloader.App.Install;
using AdobeDownloader.App.Uninstall;

namespace AdobeDownloader.App;

/// <summary>
/// 卸载执行窗口（--uninstall &lt;recordPath&gt; 模式，管理员权限）：
/// 读取安装记录，回删快捷方式、安装目录、系统卸载注册表项。
/// </summary>
public partial class UninstallRunWindow : Window
{
    private readonly string _target;
    private readonly bool _forceRemove;

    /// <summary>按安装记录卸载（--uninstall）。</summary>
    public UninstallRunWindow(string recordPath) : this(recordPath, forceRemove: false) { }

    /// <summary>forceRemove=true 时 target 为安装目录，执行强制删除（--forceremove）。</summary>
    public UninstallRunWindow(string target, bool forceRemove)
    {
        InitializeComponent();
        _target = target;
        _forceRemove = forceRemove;
        Loaded += (_, _) => Run();
    }

    private void Run()
    {
        var uninstaller = new ProductUninstaller();
        uninstaller.Logged += line => Dispatcher.Invoke(() =>
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        });
        var progress = new Progress<double>(f => Bar.Value = f * 100);

        // 强制删除：target = 安装目录
        if (_forceRemove)
        {
            TitleText.Text = $"正在强制删除：{System.IO.Path.GetFileName(_target.TrimEnd('\\', '/'))}";
            RunAction(() => uninstaller.ForceRemove(_target, progress), "强制删除");
            return;
        }

        // 按记录卸载
        var record = InstallRegistry.ReadRecord(_target);
        if (record is null)
        {
            TitleText.Text = "无法卸载";
            StatusText.Text = "找不到或无法读取安装记录，可能已被卸载。";
            CloseButton.IsEnabled = true;
            return;
        }
        TitleText.Text = $"正在卸载：{record.DisplayName}";
        RunAction(() => uninstaller.Uninstall(record, progress), "卸载");
    }

    private void RunAction(Action action, string verb)
    {
        _ = System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                action();
                Dispatcher.Invoke(() => { StatusText.Text = $"{verb}完成"; Bar.Value = 100; });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => StatusText.Text = $"{verb}失败：{ex.Message}");
            }
            finally
            {
                Dispatcher.Invoke(() => CloseButton.IsEnabled = true);
            }
        });
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
