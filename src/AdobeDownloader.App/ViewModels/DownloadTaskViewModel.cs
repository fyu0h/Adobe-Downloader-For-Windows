using System.Diagnostics;
using System.IO;
using System.Windows;
using AdobeDownloader.Core;
using AdobeDownloader.Core.Models;

namespace AdobeDownloader.App.ViewModels;

public enum TaskState { Preparing, Downloading, Paused, Completed, Failed }

/// <summary>一个下载任务的界面状态与操作。任务持久化，重启后可继续，直到用户删除。</summary>
public sealed class DownloadTaskViewModel : ObservableObject
{
    private CancellationTokenSource _cts = new();

    public string Id { get; }
    public string Title { get; }
    public string Directory { get; }
    public DownloadPlan Plan { get; }
    public DateTime CreatedAt { get; }
    public bool IsRemoved { get; private set; }

    // 供 MainViewModel 订阅：状态变化时持久化；请求继续/删除。
    public event Action<DownloadTaskViewModel>? StateChanged;
    public event Action<DownloadTaskViewModel>? ResumeRequested;
    public event Action<DownloadTaskViewModel>? RemoveRequested;

    public DownloadTaskViewModel(DownloadPlan plan, string directory,
        string? id = null, TaskState initialState = TaskState.Preparing, DateTime? createdAt = null)
    {
        Plan = plan;
        Directory = directory;
        Id = id ?? Guid.NewGuid().ToString("N");
        CreatedAt = createdAt ?? DateTime.UtcNow;
        _state = initialState;
        Title = $"{plan.DisplayName}  {plan.ProductVersion}  ({plan.Language}, {plan.Architecture.PlatformId()})";

        OpenFolderCommand = new RelayCommand(OpenFolder);
        InstallCommand = new RelayCommand(Install, () => State == TaskState.Completed);
        PauseCommand = new RelayCommand(() => _cts.Cancel(),
            () => State is TaskState.Downloading or TaskState.Preparing);
        ResumeCommand = new RelayCommand(() => ResumeRequested?.Invoke(this),
            () => State is TaskState.Paused or TaskState.Failed);
        RemoveCommand = new RelayCommand(Remove);

        Detail = initialState switch
        {
            TaskState.Paused => "已暂停，点击“继续”恢复下载",
            TaskState.Failed => "下载失败，点击“继续”重试",
            TaskState.Completed => "下载完成，可点击“安装”",
            _ => "准备中...",
        };
        if (initialState == TaskState.Completed) _progress = 100;
    }

    public CancellationToken Token => _cts.Token;

    /// <summary>继续下载前重建取消令牌。</summary>
    public void ResetToken() => _cts = new CancellationTokenSource();

    private TaskState _state;
    public TaskState State
    {
        get => _state;
        set
        {
            if (SetField(ref _state, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsActive));
                InstallCommand.RaiseCanExecuteChanged();
                PauseCommand.RaiseCanExecuteChanged();
                ResumeCommand.RaiseCanExecuteChanged();
                if (!IsRemoved) StateChanged?.Invoke(this);
            }
        }
    }

    private double _progress;
    public double Progress { get => _progress; set => SetField(ref _progress, value); }

    private string _detail = "准备中...";
    public string Detail { get => _detail; set => SetField(ref _detail, value); }

    public bool IsActive => State is TaskState.Downloading or TaskState.Preparing;

    public string StatusText => State switch
    {
        TaskState.Preparing => "准备中",
        TaskState.Downloading => "下载中",
        TaskState.Paused => "已暂停",
        TaskState.Completed => "已完成",
        TaskState.Failed => "失败",
        _ => "",
    };

    public RelayCommand OpenFolderCommand { get; }
    public RelayCommand InstallCommand { get; }
    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand RemoveCommand { get; }

    public PersistedTask ToPersisted() => new()
    {
        Id = Id,
        Plan = Plan,
        Directory = Directory,
        State = State,
        CreatedAt = CreatedAt,
    };

    public void UpdateProgress(DownloadProgress p)
    {
        Progress = p.Fraction * 100;
        var mb = 1024.0 * 1024;
        var speed = p.BytesPerSecond / mb;
        Detail = $"{p.DownloadedBytes / mb:F0} / {p.TotalBytes / mb:F0} MB   " +
                 $"{p.CompletedPackages}/{p.TotalPackages} 包   {speed:F1} MB/s";
    }

    private void Remove()
    {
        var choice = AppDialog.Show(
            "确定删除此下载任务吗？\n\n将同时删除已下载的文件，此操作无法撤销。",
            "删除任务", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.OK) return;

        IsRemoved = true;
        _cts.Cancel();
        DeleteDownloadedFiles();
        RemoveRequested?.Invoke(this);
    }

    private void DeleteDownloadedFiles()
    {
        try
        {
            // 只删本任务的下载子目录，且必须是真实子目录（防御异常路径）
            if (string.IsNullOrWhiteSpace(Directory) || Directory.TrimEnd('\\').Length <= 3
                || !System.IO.Directory.Exists(Directory))
                return;

            foreach (var f in System.IO.Directory.EnumerateFiles(Directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var a = File.GetAttributes(f);
                    if ((a & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                        File.SetAttributes(f, FileAttributes.Normal);
                }
                catch { /* ignore */ }
            }
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception ex)
        {
            AppDialog.Show($"删除已下载文件失败：{ex.Message}", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OpenFolder()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Directory}\"") { UseShellExecute = true });
        }
        catch { /* ignore */ }
    }

    private void Install()
    {
        var driver = Path.Combine(Directory, "driver.xml");
        if (!File.Exists(driver))
        {
            AppDialog.Show("找不到 driver.xml，无法安装。", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 用内置安装引擎（移植自原版 HDPIM），以管理员权限重启自身进入 --install 模式。
        try
        {
            var exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? throw new InvalidOperationException("无法确定自身可执行文件路径");
            Process.Start(new ProcessStartInfo(exe, $"--install \"{Directory}\"")
            {
                UseShellExecute = true,
                Verb = "runas", // 触发 UAC 管理员授权
            });
        }
        catch (Exception ex)
        {
            AppDialog.Show($"启动安装失败：{ex.Message}", "Adobe Downloader",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
