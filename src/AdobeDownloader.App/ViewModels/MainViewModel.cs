using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using AdobeDownloader.Core;
using AdobeDownloader.Core.Models;
using AdobeDownloader.Core.Parsing;

namespace AdobeDownloader.App.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly HttpClient _http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AppSettings _settings;
    private readonly TaskStore _taskStore = new();
    private AdobeApiClient _api;
    private CatalogResult? _catalog;

    public MainViewModel()
    {
        _settings = AppSettings.Load();
        _api = new AdobeApiClient(_http, _settings.ApiVersion);

        Products = new ObservableCollection<ProductGroupViewModel>();
        ProductsView = CollectionViewSource.GetDefaultView(Products);
        ProductsView.Filter = FilterProduct;

        Languages = new ObservableCollection<LanguageOption>(
            AppStatics.SupportedLanguages.Select(l => new LanguageOption(l.Code, l.Name)));
        SelectedLanguage = Languages.FirstOrDefault(l => l.Code == _settings.DefaultLanguage) ?? Languages[0];

        Architectures = new ObservableCollection<ArchOption>(new[]
        {
            new ArchOption(TargetArchitecture.X64),
            new ArchOption(TargetArchitecture.Arm64),
        });
        SelectedArchitecture = Architectures.FirstOrDefault(a => a.Value == _settings.GetArchitecture())
                               ?? Architectures[0];

        DownloadTasks = new ObservableCollection<DownloadTaskViewModel>();

        RefreshCommand = new AsyncRelayCommand(LoadCatalogAsync, () => !IsBusy);
        DownloadCommand = new AsyncRelayCommand(StartDownloadAsync,
            () => SelectedVersion is not null && !IsBusy);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);

        LoadPersistedTasks();
        // 注：SelectedArchitecture 赋值时其 setter 已触发 LoadCachedCatalog，
        // 启动即从本地缓存显示产品，无需每次联网刷新。

        StartBackgroundScanInstalledApps();
    }

    /// <summary>启动时后台扫描本机已安装的 Adobe 程序并存储结果，供卸载窗口秒开。</summary>
    private static void StartBackgroundScanInstalledApps()
    {
        Uninstall.InstalledAppsStore.LoadFromDisk(); // 先载入上次结果，卸载窗口可立即展示
        _ = Task.Run(() =>
        {
            try
            {
                var apps = Uninstall.InstalledAppScanner.Scan();
                Uninstall.InstalledAppsStore.Save(apps);
            }
            catch { /* 扫描失败不影响主流程 */ }
        });
    }

    /// <summary>从本地缓存加载当前架构的产品目录（若有）。启动及切换架构时调用。</summary>
    private void LoadCachedCatalog()
    {
        var arch = SelectedArchitecture.Value;
        var cached = CatalogCache.Load(arch);
        if (cached is null) return;
        try
        {
            var catalog = CatalogParser.Parse(cached.Value.Xml, visibleChannel: "ccm");
            _catalog = catalog;
            PopulateProducts(catalog, arch);
            Status = $"已从本地缓存加载 {Products.Count} 个产品" +
                     $"（{cached.Value.SavedAt:yyyy-MM-dd HH:mm} 缓存，点击“刷新目录”获取最新）";
        }
        catch
        {
            /* 缓存损坏则忽略，用户可手动刷新 */
        }
    }

    /// <summary>用目录结果填充左侧产品列表。</summary>
    private void PopulateProducts(CatalogResult catalog, TargetArchitecture arch)
    {
        var groups = catalog.Products
            .Where(p => p.HasValidVersions(new[] { "win64", "winarm64" }))
            .GroupBy(p => p.Id)
            .Select(g => new ProductGroupViewModel(g.Key, g.First().DisplayName, g))
            .OrderBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Products.Clear();
        foreach (var g in groups) Products.Add(g);
    }

    /// <summary>启动时从磁盘恢复未删除的下载任务（未完成的显示为已暂停，可继续）。</summary>
    private void LoadPersistedTasks()
    {
        foreach (var pt in _taskStore.Load().OrderBy(t => t.CreatedAt))
        {
            // 进行中/准备中的任务重启后视为已暂停
            var state = pt.State switch
            {
                TaskState.Downloading or TaskState.Preparing => TaskState.Paused,
                var s => s,
            };
            var task = new DownloadTaskViewModel(pt.Plan, pt.Directory, pt.Id, state, pt.CreatedAt);
            HookTask(task);
            DownloadTasks.Insert(0, task);
        }
    }

    private void HookTask(DownloadTaskViewModel task)
    {
        task.StateChanged += _ => SaveTasks();
        task.ResumeRequested += ResumeTask;
        task.RemoveRequested += RemoveTask;
    }

    private void SaveTasks()
        => _taskStore.Save(DownloadTasks.Where(t => !t.IsRemoved).Select(t => t.ToPersisted()));

    private void RemoveTask(DownloadTaskViewModel task)
    {
        Application.Current?.Dispatcher.Invoke(() => DownloadTasks.Remove(task));
        SaveTasks();
    }

    private void ResumeTask(DownloadTaskViewModel task)
    {
        task.ResetToken();
        _ = RunTaskAsync(task);
    }

    // ---- 目录与产品 ----

    public ObservableCollection<ProductGroupViewModel> Products { get; }
    public ICollectionView ProductsView { get; }

    private ProductGroupViewModel? _selectedProduct;
    public ProductGroupViewModel? SelectedProduct
    {
        get => _selectedProduct;
        set
        {
            if (SetField(ref _selectedProduct, value))
            {
                AvailableVersions.Clear();
                if (value is not null)
                    foreach (var v in value.Versions) AvailableVersions.Add(v);
                SelectedVersion = AvailableVersions.FirstOrDefault();
                OnPropertyChanged(nameof(HasSelection));
            }
        }
    }

    public ObservableCollection<Product> AvailableVersions { get; } = new();

    private Product? _selectedVersion;
    public Product? SelectedVersion
    {
        get => _selectedVersion;
        set { if (SetField(ref _selectedVersion, value)) DownloadCommand.RaiseCanExecuteChanged(); }
    }

    public bool HasSelection => SelectedProduct is not null;

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (SetField(ref _searchText, value)) ProductsView.Refresh(); }
    }

    private bool FilterProduct(object obj)
    {
        if (obj is not ProductGroupViewModel p) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return p.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase)
               || p.Id.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    // ---- 选项 ----

    public ObservableCollection<LanguageOption> Languages { get; }
    private LanguageOption _selectedLanguage = null!;
    public LanguageOption SelectedLanguage
    {
        get => _selectedLanguage;
        set { if (SetField(ref _selectedLanguage, value)) { _settings.DefaultLanguage = value.Code; _settings.Save(); } }
    }

    public ObservableCollection<ArchOption> Architectures { get; }
    private ArchOption _selectedArchitecture = null!;
    public ArchOption SelectedArchitecture
    {
        get => _selectedArchitecture;
        set
        {
            if (SetField(ref _selectedArchitecture, value))
            {
                _settings.Architecture = value.Value.ToString();
                _settings.Save();
                LoadCachedCatalog(); // 切换架构时加载该架构的本地缓存
            }
        }
    }

    public string DownloadDirectory
    {
        get => _settings.DownloadDirectory;
        set { _settings.DownloadDirectory = value; _settings.Save(); OnPropertyChanged(); }
    }

    // ---- 任务 ----

    public ObservableCollection<DownloadTaskViewModel> DownloadTasks { get; }

    // ---- 状态 ----

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
                DownloadCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private string _status = "点击“刷新目录”从 Adobe 获取产品列表";
    public string Status { get => _status; set => SetField(ref _status, value); }

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand DownloadCommand { get; }
    public RelayCommand BrowseFolderCommand { get; }

    // ---- 逻辑 ----

    private async Task LoadCatalogAsync()
    {
        IsBusy = true;
        Status = "正在从 Adobe 获取产品目录...";
        try
        {
            var arch = SelectedArchitecture.Value;
            var xml = await Task.Run(() => _api.FetchCatalogXmlAsync(arch));
            CatalogCache.Save(arch, xml); // 写入本地缓存，下次启动直接用
            var catalog = CatalogParser.Parse(xml, visibleChannel: "ccm");
            _catalog = catalog;

            PopulateProducts(catalog, arch);
            Status = $"已加载 {Products.Count} 个产品（架构 {arch.DisplayName()}），已缓存到本地";
        }
        catch (Exception ex)
        {
            Status = $"获取目录失败：{ex.Message}";
            AppDialog.Show(ex.Message, "获取目录失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StartDownloadAsync()
    {
        if (_catalog is null || SelectedVersion is null) return;

        var product = SelectedVersion;
        var language = SelectedLanguage.Code;
        var arch = SelectedArchitecture.Value;

        IsBusy = true;
        Status = $"正在准备 {product.DisplayName} {product.Version} 的下载计划...";
        try
        {
            var builder = new PlanBuilder(_api, _catalog.DependencyPool);
            var status = new Progress<string>(s => Status = s);
            var plan = await Task.Run(() => builder.BuildAsync(product, language, arch, status));

            if (plan.TotalPackages == 0)
            {
                Status = "该产品在当前语言/架构下没有可下载的包";
                AppDialog.Show("没有可下载的包，请尝试其它语言或架构。", "Adobe Downloader",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var folderName = $"{product.Id}_{product.Version}_{language}_{arch.PlatformId()}";
            var destination = System.IO.Path.Combine(DownloadDirectory, folderName);

            plan.Cdn = _catalog.Cdn; // 随任务持久化，供重启后恢复下载

            var task = new DownloadTaskViewModel(plan, destination);
            HookTask(task);
            DownloadTasks.Insert(0, task);
            SaveTasks();
            Status = $"已创建下载任务：{plan.DisplayName} {plan.ProductVersion}（{plan.TotalDownloadSize / 1024 / 1024} MB）";

            _ = RunTaskAsync(task);
        }
        catch (Exception ex)
        {
            Status = $"创建下载任务失败：{ex.Message}";
            AppDialog.Show(ex.Message, "Adobe Downloader", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunTaskAsync(DownloadTaskViewModel task)
    {
        // 恢复下载用任务持久化的 CDN（重启后 _catalog 可能为空）
        var cdn = !string.IsNullOrEmpty(task.Plan.Cdn) ? task.Plan.Cdn : _catalog?.Cdn ?? "";
        var engine = new DownloadEngine(cdn, _http, _settings.ApiVersion);
        var progress = new Progress<DownloadProgress>(task.UpdateProgress);
        task.State = TaskState.Downloading;
        try
        {
            await Task.Run(() => engine.DownloadAsync(
                task.Plan, task.Directory, progress, _settings.InstallDirectory, task.Token));
            task.Progress = 100;
            task.State = TaskState.Completed;
            task.Detail = "下载完成，可点击“安装”";
        }
        catch (OperationCanceledException)
        {
            if (task.IsRemoved) return;         // 被删除，不改状态
            task.State = TaskState.Paused;       // 暂停：断点续传，可继续
            task.Detail = "已暂停，点击“继续”恢复下载";
        }
        catch (Exception ex)
        {
            task.State = TaskState.Failed;
            task.Detail = $"失败：{ex.Message}（点击“继续”重试）";
        }
    }

    private void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "选择下载目录",
            InitialDirectory = System.IO.Directory.Exists(DownloadDirectory)
                ? DownloadDirectory
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog() == true)
            DownloadDirectory = dialog.FolderName;
    }
}

public sealed record LanguageOption(string Code, string Name)
{
    public string Display => $"{Name} ({Code})";
}

public sealed class ArchOption
{
    public TargetArchitecture Value { get; }
    public ArchOption(TargetArchitecture value) => Value = value;
    public string Display => Value.DisplayName();
}
