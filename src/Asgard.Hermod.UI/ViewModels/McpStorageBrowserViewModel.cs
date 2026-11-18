namespace Asgard.Hermod.UI.ViewModels;

/// <summary>用户主动浏览 Bucket 与文件夹，只有确认后才把范围交还授权表单。</summary>
public sealed class McpStorageBrowserViewModel : ObservableObject, IDisposable
{
    private readonly IMcpStorageScopeBrowser _browser;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _request;
    private McpStorageBucket? _bucket;
    private string? _next;
    private string _prefix = "", _message = "";
    private McpStorageFolderChoice? _selectedFolder;
    private int _generation;
    private bool _busy, _disposed;

    /// <summary>冻结打开弹窗时的工作区与连接版本，避免迟到结果修改其他草稿。</summary>
    public McpStorageBrowserViewModel(IMcpStorageScopeBrowser browser, string owner, McpTarget target)
    {
        _browser = browser; Owner = owner; Target = target;
        OpenFolderCommand = new(_ => NavigateAsync(SelectedFolder!.Prefix), _ => CanNavigate && SelectedFolder is not null);
        UpCommand = new(_ => NavigateAsync(Parent(Prefix)), _ => CanNavigate && Prefix.Length > 0);
        LoadMoreCommand = new(_ => LoadPageAsync(true), _ => CanNavigate && _next is not null);
        RefreshCommand = new(_ => SelectedBucket is null ? InitializeAsync() : NavigateAsync(Prefix), _ => !_disposed && !IsBusy);
    }

    /// <summary>打开时的工作区。</summary>
    public string Owner { get; }
    /// <summary>打开时的实例版本。</summary>
    public McpTarget Target { get; }
    /// <summary>只展示真实服务返回的 Bucket。</summary>
    public ObservableCollection<McpStorageBucket> Buckets { get; } = [];
    /// <summary>当前目录已经加载的直接子文件夹。</summary>
    public ObservableCollection<McpStorageFolderChoice> Folders { get; } = [];
    /// <summary>选择 Bucket 后进入其根目录，不默认选择整个账户。</summary>
    public McpStorageBucket? SelectedBucket
    {
        get => _bucket;
        set
        {
            if (value is not null && !Buckets.Contains(value) || !SetProperty(ref _bucket, value)) return;
            _prefix = ""; _next = null; SelectedFolder = null; Folders.Clear();
            OnPropertyChanged(nameof(Prefix)); OnPropertyChanged(nameof(Location)); NotifyCommands();
            if (value is not null) _ = NavigateAsync("");
        }
    }
    /// <summary>目录前缀为空表示整个 Bucket，非空时保留结尾斜杠。</summary>
    public string Prefix { get => _prefix; private set { if (SetProperty(ref _prefix, value)) OnPropertyChanged(nameof(Location)); } }
    /// <summary>完整显示将要授权的位置。</summary>
    public string Location => SelectedBucket is null ? Text.Get("McpStorageBrowserSelectBucket")
        : Text.Format("McpStorageBrowserLocation", SelectedBucket.Name, ChosenPrefix.Length == 0 ? Text.Get("McpStorageBucketRoot") : ChosenPrefix);
    /// <summary>选择子文件夹即可授权该前缀；未选择时授权当前目录。</summary>
    public string ChosenPrefix => SelectedFolder?.Prefix ?? Prefix;
    /// <summary>用户选择的直接子目录。</summary>
    public McpStorageFolderChoice? SelectedFolder { get => _selectedFolder; set { if (SetProperty(ref _selectedFolder, value)) { OnPropertyChanged(nameof(ChosenPrefix)); OnPropertyChanged(nameof(Location)); NotifyCommands(); } } }
    /// <summary>安全显示读取错误与加载状态。</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    /// <summary>读取期间禁止提交不完整的选择。</summary>
    public bool IsBusy { get => _busy; private set { if (SetProperty(ref _busy, value)) { OnPropertyChanged(nameof(CanChoose)); OnPropertyChanged(nameof(CanInteract)); NotifyCommands(); } } }
    /// <summary>仅已选择 Bucket 后允许确认当前范围。</summary>
    public bool CanChoose => !_disposed && !IsBusy && SelectedBucket is not null;
    /// <summary>读取时不允许更换 Bucket。</summary>
    public bool CanInteract => !_disposed && !IsBusy;
    private bool CanNavigate => CanChoose;
    /// <summary>进入选中的子文件夹。</summary>
    public AsyncRelayCommand OpenFolderCommand { get; }
    /// <summary>返回上一级目录。</summary>
    public AsyncRelayCommand UpCommand { get; }
    /// <summary>读取当前目录的下一页。</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }
    /// <summary>重新读取失败的 Bucket 或目录。</summary>
    public AsyncRelayCommand RefreshCommand { get; }

    /// <summary>只在弹窗显示后执行只读列举。</summary>
    public async Task InitializeAsync()
    {
        if (_disposed) return;
        using var request = BeginRequest();
        int generation = _generation;
        try
        {
            var buckets = await _browser.ListBucketsAsync(Owner, Target, request.Token);
            if (!Current(generation)) return;
            Buckets.Clear(); foreach (var bucket in buckets) Buckets.Add(bucket);
            Message = buckets.Count == 0 ? Text.Get("McpStorageNoBuckets") : "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { if (Current(generation)) Message = Text.Error(error, "McpStorageBrowseFailed"); }
        finally { Finish(generation); }
    }

    /// <summary>导航时清除旧列表；保留完整路径，即使读取失败也不会扩大选择。</summary>
    public Task NavigateAsync(string prefix)
    {
        if (_disposed || SelectedBucket is null) return Task.CompletedTask;
        Prefix = prefix; _next = null; SelectedFolder = null; Folders.Clear(); NotifyCommands();
        return LoadPageAsync(false);
    }

    private async Task LoadPageAsync(bool more)
    {
        if (_disposed || SelectedBucket is null) return;
        var bucket = SelectedBucket;
        string prefix = Prefix;
        string? cursor = more ? _next : null;
        using var request = BeginRequest();
        int generation = _generation;
        try
        {
            var page = await _browser.ListFoldersAsync(Owner, Target, bucket, prefix, cursor, request.Token);
            if (!Current(generation)) return;
            foreach (string folder in page.Folders)
                if (!Folders.Any(item => item.Prefix == folder)) Folders.Add(new(folder, folder[prefix.Length..].TrimEnd('/')));
            _next = page.NextToken;
            Message = Folders.Count == 0 && _next is null ? Text.Get("McpStorageNoFolders") : "";
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested) { }
        catch (Exception error) { if (Current(generation)) Message = Text.Error(error, "McpStorageBrowseFailed"); }
        finally { Finish(generation); }
    }

    private CancellationTokenSource BeginRequest()
    {
        _request?.Cancel();
        _request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _generation++; IsBusy = true; Message = Text.Get("McpStorageBrowsing");
        return _request;
    }
    private bool Current(int generation) => !_disposed && generation == _generation;
    private void Finish(int generation)
    {
        if (!Current(generation)) return;
        _request = null; IsBusy = false; NotifyCommands();
    }
    private static string Parent(string prefix)
    {
        int slash = prefix.TrimEnd('/').LastIndexOf('/');
        return slash < 0 ? "" : prefix[..(slash + 1)];
    }
    private void NotifyCommands()
    {
        OnPropertyChanged(nameof(CanChoose));
        OpenFolderCommand.NotifyCanExecuteChanged(); UpCommand.NotifyCanExecuteChanged();
        LoadMoreCommand.NotifyCanExecuteChanged(); RefreshCommand.NotifyCanExecuteChanged();
    }
    /// <summary>关闭后取消列举并忽略迟到结果。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); _lifetime.Dispose(); NotifyCommands();
    }
}
