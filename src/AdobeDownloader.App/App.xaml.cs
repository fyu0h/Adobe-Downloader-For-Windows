using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using AdobeDownloader.Core.Cleanup;

namespace AdobeDownloader.App;

/// <summary>
/// 应用入口。带 --install "&lt;driverDir&gt;" 参数时进入管理员安装模式，--cleanup 进入清理执行模式，
/// 否则打开主窗口。主窗口限制单实例；安装/清理为提权子进程，允许并存。
/// </summary>
public partial class App : Application
{
    private static Mutex? _instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;
        var installIdx = Array.FindIndex(args, a => a.Equals("--install", StringComparison.OrdinalIgnoreCase));
        if (installIdx >= 0 && installIdx + 1 < args.Length)
        {
            new InstallWindow(args[installIdx + 1]).Show();
            return;
        }

        // 卸载自装产品：--uninstall "<记录JSON路径>"。可能由系统“应用”列表调用（非管理员），故自提权。
        var uninstallIdx = Array.FindIndex(args, a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));
        if (uninstallIdx >= 0 && uninstallIdx + 1 < args.Length)
        {
            var recordPath = args[uninstallIdx + 1];
            if (!IsElevated())
            {
                RelaunchElevated($"--uninstall \"{recordPath}\"");
                Shutdown();
                return;
            }
            new UninstallRunWindow(recordPath).Show();
            return;
        }

        // 强制删除自装/残留 Adobe 产品：--forceremove "<安装目录>"。同样自提权。
        var forceIdx = Array.FindIndex(args, a => a.Equals("--forceremove", StringComparison.OrdinalIgnoreCase));
        if (forceIdx >= 0 && forceIdx + 1 < args.Length)
        {
            var installDir = args[forceIdx + 1];
            if (!IsElevated())
            {
                RelaunchElevated($"--forceremove \"{installDir}\"");
                Shutdown();
                return;
            }
            new UninstallRunWindow(installDir, forceRemove: true).Show();
            return;
        }

        // 直接打开清理选择窗口（供快捷方式/调试）
        if (args.Any(a => a.Equals("--cleanup-ui", StringComparison.OrdinalIgnoreCase)))
        {
            new CleanupWindow().Show();
            return;
        }

        // 管理员清理执行模式：--cleanup Opt1,Opt2,...
        var cleanupIdx = Array.FindIndex(args, a => a.Equals("--cleanup", StringComparison.OrdinalIgnoreCase));
        if (cleanupIdx >= 0 && cleanupIdx + 1 < args.Length)
        {
            var options = args[cleanupIdx + 1]
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => Enum.TryParse<CleanupOption>(s.Trim(), out var o) ? (CleanupOption?)o : null)
                .Where(o => o is not null).Select(o => o!.Value).ToList();
            new CleanupRunWindow(options).Show();
            return;
        }

        // 主窗口：单实例。已在运行则激活已有窗口并退出。
        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\AdobeDownloader_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            ActivateExistingWindow();
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private static void ActivateExistingWindow()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(current.ProcessName))
            {
                if (p.Id == current.Id || p.MainWindowHandle == IntPtr.Zero) continue;
                ShowWindow(p.MainWindowHandle, SW_RESTORE);
                SetForegroundWindow(p.MainWindowHandle);
                break;
            }
        }
        catch { /* 尽力而为 */ }
    }

    private static bool IsElevated()
    {
        using var id = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchElevated(string arguments)
    {
        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is null) return;
            Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas" });
        }
        catch { /* 用户取消 UAC 授权 */ }
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
