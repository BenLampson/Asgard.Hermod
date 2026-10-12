namespace Asgard.Hermod.ObjectStorage.UI.ViewModels;

/// <summary>按当前 Bucket 和前缀浏览真实对象，导航请求可以互相取消而不锁住页面。</summary>
public sealed partial class ObjectStorageWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly InstanceItemViewModel _instance;
    private readonly IObjectStorageService _service;
    private readonly IObjectStorageDialogService _dialogs;
    private readonly IInstanceEditor? _editor;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _transferGate = new(1, 1);
    private CancellationTokenSource? _listOperation;
    private CancellationTokenSource? _bucketOperation;
    private CancellationTokenSource? _detailOperation;
    private CancellationTokenSource? _searchOperation;
    private Task? _initializeTask;
    private int _navigationVersion;
    private int _bucketVersion;
    private int _detailVersion;
    private ObjectStorageBucket? _selectedBucket;
    private ObjectStorageEntryViewModel? _selectedEntry;
    private ObjectStorageObjectDetails? _details;
    private string _prefix = "";
    private string _searchText = "";
    private string _readError = "";
    private string _actionError = "";
    private string _detailError = "";
    private string? _nextToken;
    private bool _isLoading;
    private bool _isDetailLoading;
    private bool _isInitialized;
    private bool _hasConnected;
    private bool _dialogOpen;
    private bool _isTransferExpanded;

    /// <summary>页面只依赖安全实例摘要、存储服务及可替换的交互服务。</summary>
    public ObjectStorageWorkspaceViewModel(InstanceItemViewModel instance, IObjectStorageService service,
        IObjectStorageDialogService dialogs, IInstanceEditor? editor = null)
        : base("instance:" + instance.Id, instance.DisplayName, instance.Icon, instance.Id, hasAlert: instance.HasAlert)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(dialogs);
        _instance = instance; _service = service; _dialogs = dialogs; _editor = editor;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !IsClosed);
        LoadMoreCommand = new AsyncRelayCommand(_ => LoadPageAsync(true), _ => !IsClosed && !IsLoading && _nextToken is not null);
        UploadCommand = new AsyncRelayCommand(_ => SelectUploadAsync(), _ => CanOperate);
        NewFolderCommand = new AsyncRelayCommand(_ => CreateFolderAsync(), _ => CanOperate);
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, parameter => CanOperate && Target(parameter) is not null);
        BatchDownloadCommand = new AsyncRelayCommand(_ => DownloadCheckedAsync(), _ => CanOperate && HasCheckedEntries);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, parameter => CanOperate && (Target(parameter) is not null || HasCheckedEntries));
        TemporaryLinkCommand = new AsyncRelayCommand(ShowTemporaryLinkAsync, parameter => CanOperate && Target(parameter) is { IsDirectory: false });
        CopyKeyCommand = new RelayCommand(parameter => CopyText(Target(parameter)?.Key), parameter => !IsClosed && Target(parameter) is not null);
        CopyPreviewCommand = new RelayCommand(_ => CopyText(PreviewText), _ => !IsClosed && HasTextPreview);
        ClearSelectionCommand = new RelayCommand(_ => { foreach (var entry in Entries) entry.IsChecked = false; });
        CloseDetailsCommand = new RelayCommand(_ => SelectedEntry = null);
        ClearErrorCommand = new RelayCommand(_ => ActionError = "");
        ClearSearchCommand = new AsyncRelayCommand(_ => SearchAsync(""), _ => !IsClosed && SearchText.Length > 0);
        EditConnectionCommand = new AsyncRelayCommand(_ => EditConnectionAsync(), _ => !IsClosed && !_dialogOpen && _editor is not null && !Transfers.Any(task => task.IsRunning));
    }

    /// <summary>当前目录完成导航或读取，通知页面订阅者。</summary>
    public event EventHandler? NavigationChanged;
    /// <summary>容器读取或编辑连接后通知页面订阅者。</summary>
    public event EventHandler? BucketsChanged;
    /// <summary>服务返回的可见容器，不伪造示例数据。</summary>
    public ObservableCollection<ObjectStorageBucket> Buckets { get; } = [];
    /// <summary>当前页和继续加载得到的对象，不统计整个 Bucket。</summary>
    public ObservableCollection<ObjectStorageEntryViewModel> Entries { get; } = [];
    /// <summary>当前前缀的可点击路径。</summary>
    public ObservableCollection<ObjectStorageBreadcrumbViewModel> Breadcrumbs { get; } = [];
    /// <summary>右侧容器选择框后只显示目录段，避免重复展示 Bucket 名称。</summary>
    public IEnumerable<ObjectStorageBreadcrumbViewModel> DirectoryBreadcrumbs => Breadcrumbs.Skip(1);
    /// <summary>失败、取消和完成的任务都保留在当前页签。</summary>
    public ObservableCollection<ObjectStorageTransferViewModel> Transfers { get; } = [];
    /// <summary>实例编辑后立即更新标题。</summary>
    public string Name => _instance.DisplayName;
    /// <summary>区域随当前容器变化，未选择时仅显示服务身份。</summary>
    public string Subtitle => (string.IsNullOrWhiteSpace(_instance.Summary.Endpoint) ? "对象存储" : _instance.Summary.Endpoint)
        + (SelectedBucket is null ? "" : " / " + SelectedBucket.Region);
    /// <summary>保存的环境及自定义标签。</summary>
    public IReadOnlyList<string> Tags => _instance.Tags;
    /// <summary>只有实际读取成功才呈现已连接。</summary>
    public string Status => ReadError.Length > 0 ? "读取失败" : _hasConnected ? "已连接" : IsLoading ? "连接中" : "已配置";
    /// <summary>保存状态和真实访问成功使用不同色调。</summary>
    public StatusTone StatusTone => ReadError.Length > 0 ? StatusTone.Warning : _hasConnected ? StatusTone.Success : StatusTone.Muted;
    /// <summary>选择容器会进入根目录，旧的详情和勾选同步清除。</summary>
    public ObjectStorageBucket? SelectedBucket
    {
        get => _selectedBucket;
        set { if (value is not null && value != _selectedBucket && !IsClosed) _ = NavigateAsync(value, ""); }
    }
    /// <summary>当前目录前缀，非空时包含结尾的斜杠。</summary>
    public string Prefix => _prefix;
    /// <summary>当前上传、删除或创建窗口中的明确位置。</summary>
    public string Location => (SelectedBucket?.Name ?? "") + (Prefix.Length == 0 ? "" : " / " + Prefix.TrimEnd('/').Replace("/", " / ", StringComparison.Ordinal));
    /// <summary>输入搜索在当前目录内执行，新的输入取消旧查询。</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) { ClearSearchCommand.NotifyCanExecuteChanged(); _ = SearchAfterDelayAsync(); } }
    }
    /// <summary>单击对象读取预览，勾选不改变详情。</summary>
    public ObjectStorageEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        set { if (SetProperty(ref _selectedEntry, value)) { NotifyDetails(); _ = LoadDetailsAsync(value); } }
    }
    /// <summary>元数据和预览的安全快照。</summary>
    public ObjectStorageObjectDetails? Details { get => _details; private set { if (SetProperty(ref _details, value)) NotifyDetails(); } }
    /// <summary>列表失败与操作失败独立，操作失败不会清空已读对象。</summary>
    public string ReadError { get => _readError; private set { if (SetProperty(ref _readError, value)) NotifyListState(); } }
    /// <summary>上传准备、复制或其他操作失败的可重试说明。</summary>
    public string ActionError { get => _actionError; private set => SetProperty(ref _actionError, value); }
    /// <summary>详情读取失败不锁住对象列表。</summary>
    public string DetailError { get => _detailError; private set { if (SetProperty(ref _detailError, value)) NotifyDetails(); } }
    /// <summary>仅当前列表请求控制加载状态，过期请求不会结束新请求的加载指示。</summary>
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) NotifyListState(); } }
    /// <summary>当前对象元数据仍在读取。</summary>
    public bool IsDetailLoading { get => _isDetailLoading; private set { if (SetProperty(ref _isDetailLoading, value)) NotifyDetails(); } }
    /// <summary>已经成功读取容器列表。</summary>
    public bool IsInitialized { get => _isInitialized; private set { if (SetProperty(ref _isInitialized, value)) NotifyListState(); } }
    /// <summary>批量操作统计。</summary>
    public int CheckedCount => Entries.Count(entry => entry.IsChecked);
    /// <summary>批量操作栏仅在有勾选时展示。</summary>
    public bool HasCheckedEntries => CheckedCount > 0;
    /// <summary>标题行的全选支持混合状态。</summary>
    public bool? IsAllChecked
    {
        get => Entries.Count == 0 || CheckedCount == 0 ? false : CheckedCount == Entries.Count ? true : null;
        set { foreach (var entry in Entries) entry.IsChecked = value == true; NotifyChecked(); }
    }
    /// <summary>本页已经读取的文件夹与对象数量。</summary>
    public string ListSummary => $"{Entries.Count(entry => entry.IsDirectory)} 个文件夹 · {Entries.Count(entry => !entry.IsDirectory)} 个对象";
    /// <summary>当前查询仍有下一批，宿主不能把当前行当成目录的完整清单。</summary>
    public bool HasMore => _nextToken is not null;
    /// <summary>失败时仍保留恢复入口；有对象时错误作为行上方提示。</summary>
    public bool ShowState => Entries.Count == 0;
    /// <summary>空目录允许上传和创建；无可见容器时只提供连接恢复。</summary>
    public bool ShowEmptyActions => !IsLoading && ReadError.Length == 0 && SelectedBucket is not null && Entries.Count == 0 && SearchText.Length == 0;
    /// <summary>错误和无容器状态显示恢复操作。</summary>
    public bool ShowRecovery => !IsLoading && (ReadError.Length > 0 || IsInitialized && Buckets.Count == 0);
    /// <summary>读取状态标题。</summary>
    public string StateTitle => IsLoading ? "正在读取对象…" : ReadError.Length > 0 ? "无法读取 Bucket"
        : IsInitialized && Buckets.Count == 0 ? "没有可访问的 Bucket" : SearchText.Length > 0 ? "没有匹配的对象" : "这个目录还没有对象";
    /// <summary>失败、搜索和空目录分别给出明确下一步。</summary>
    public string StateDescription => IsLoading ? "按当前目录分批加载对象。" : ReadError.Length > 0 ? ReadError
        : IsInitialized && Buckets.Count == 0 ? "请检查网络和 RAM 权限，或编辑连接。"
        : SearchText.Length > 0 ? "仅搜索当前目录中的文件名前缀。" : "上传文件，或新建一个文件夹。";
    /// <summary>展开状态由用户控制，新增任务自动展开。</summary>
    public bool IsTransferExpanded { get => _isTransferExpanded; set => SetProperty(ref _isTransferExpanded, value); }
    /// <summary>传输折叠栏显示活动及失败任务数。</summary>
    public string TransferSummary => Transfers.Any(task => task.IsRunning || task.IsFailed)
        ? $"{Transfers.Count(task => task.IsRunning)} 个进行中 · {Transfers.Count(task => task.IsFailed)} 个失败" : "无进行中的任务";
    /// <summary>编辑连接不能静默改变在途任务的凭据和目标。</summary>
    public string EditConnectionHint => Transfers.Any(task => task.IsRunning) ? "请先完成或取消传输，再编辑连接。" : "编辑连接";
    /// <summary>当前操作允许进入选择或确认窗体，浏览导航无需等待这些操作。</summary>
    private bool CanOperate => !IsClosed && !_dialogOpen && SelectedBucket is not null;

    /// <summary>首次激活懒加载，重复调用复用同一读取任务。</summary>
    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosed || IsInitialized) return Task.CompletedTask;
        if (_initializeTask?.IsCompleted == true) _initializeTask = null;
        return _initializeTask ??= ReadBucketsAsync(cancellationToken);
    }

    /// <summary>强制重新读取容器和连接，同步右侧选择器及目录路径。</summary>
    public async Task RefreshBucketsAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosed) return;
        _bucketOperation?.Cancel(); _listOperation?.Cancel(); _detailOperation?.Cancel(); _searchOperation?.Cancel();
        ++_bucketVersion; ++_navigationVersion;
        IsInitialized = false; _hasConnected = false;
        _selectedBucket = null; _prefix = ""; _nextToken = null;
        OnPropertyChanged(nameof(SelectedBucket)); OnPropertyChanged(nameof(Prefix));
        OnPropertyChanged(nameof(Location)); OnPropertyChanged(nameof(Subtitle));
        SelectedEntry = null; Entries.Clear(); Buckets.Clear(); Breadcrumbs.Clear();
        OnPropertyChanged(nameof(DirectoryBreadcrumbs));
        NotifyChecked();
        BucketsChanged?.Invoke(this, EventArgs.Empty);
        _initializeTask = ReadBucketsAsync(cancellationToken);
        await _initializeTask;
    }

    /// <summary>容器读取失败保留错误，重试时明确重新请求。</summary>
    private async Task ReadBucketsAsync(CancellationToken cancellationToken)
    {
        int version = ++_bucketVersion;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _bucketOperation?.Cancel(); _bucketOperation = operation;
        IsLoading = true; ReadError = "";
        try
        {
            var buckets = await _service.ListBucketsAsync(_instance.Id, operation.Token);
            if (IsClosed || operation.IsCancellationRequested || version != _bucketVersion) return;
            Buckets.Clear();
            foreach (var bucket in buckets) Buckets.Add(bucket);
            _hasConnected = true; IsInitialized = true;
            BucketsChanged?.Invoke(this, EventArgs.Empty);
            if (Buckets.Count > 0) await NavigateAsync(Buckets[0], "", operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { if (!IsClosed && version == _bucketVersion) ReadError = ObjectStoragePresentation.Error(exception, "请检查网络和 RAM 权限，确认可以列出此 Bucket 的对象。"); }
        finally
        {
            if (ReferenceEquals(_bucketOperation, operation)) _bucketOperation = null;
            if (!IsClosed && version == _bucketVersion && _listOperation is null) IsLoading = false;
        }
    }

    /// <summary>切目录立即清除旧选择，并取消前一次列表与详情读取。</summary>
    public async Task NavigateAsync(ObjectStorageBucket bucket, string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bucket);
        if (IsClosed) return;
        _searchOperation?.Cancel();
        _listOperation?.Cancel();
        _detailOperation?.Cancel();
        ++_navigationVersion;
        _selectedBucket = bucket;
        _prefix = prefix.Length == 0 || prefix.EndsWith('/') ? prefix : prefix + "/";
        _searchText = "";
        OnPropertyChanged(nameof(SelectedBucket)); OnPropertyChanged(nameof(Prefix)); OnPropertyChanged(nameof(SearchText));
        OnPropertyChanged(nameof(Location)); OnPropertyChanged(nameof(Subtitle));
        SelectedEntry = null; Details = null;
        Entries.Clear(); _nextToken = null;
        RebuildBreadcrumbs(); NotifyChecked(); IsLoading = true; NotifyListState();
        NavigationChanged?.Invoke(this, EventArgs.Empty);
        await LoadPageAsync(false, cancellationToken);
    }

    /// <summary>显式搜索入口供快捷键和测试使用，不改变当前 Bucket 与目录。</summary>
    public async Task SearchAsync(string search, CancellationToken cancellationToken = default)
    {
        if (IsClosed || SelectedBucket is null) return;
        _searchOperation?.Cancel();
        _searchText = search; OnPropertyChanged(nameof(SearchText));
        ClearSearchCommand.NotifyCanExecuteChanged();
        _listOperation?.Cancel(); ++_navigationVersion;
        SelectedEntry = null; Entries.Clear(); NotifyChecked();
        await LoadPageAsync(false, cancellationToken);
    }

    /// <summary>输入短暂合并后搜索；输入变化即取消旧列表，防止旧结果闪回。</summary>
    private async Task SearchAfterDelayAsync()
    {
        if (IsClosed || SelectedBucket is null) return;
        _searchOperation?.Cancel(); _listOperation?.Cancel(); ++_navigationVersion;
        _nextToken = null; NotifyListState();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _searchOperation = operation;
        try
        {
            await Task.Delay(250, operation.Token);
            if (!operation.IsCancellationRequested) await SearchAsync(SearchText, _lifetime.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        finally { if (ReferenceEquals(_searchOperation, operation)) _searchOperation = null; }
    }

    /// <summary>继续加载只使用当前查询的游标，并按完整对象键去重。</summary>
    private async Task LoadPageAsync(bool append, CancellationToken cancellationToken = default)
    {
        if (IsClosed || SelectedBucket is null) return;
        int version = _navigationVersion;
        var bucket = SelectedBucket;
        var prefix = Prefix;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _listOperation = operation;
        if (!append) _nextToken = null;
        IsLoading = true; ReadError = "";
        NotifyListState();
        bool loaded = false;
        try
        {
            var page = await _service.ListObjectsAsync(_instance.Id, bucket, prefix, SearchText, append ? _nextToken : null, operation.Token);
            if (IsClosed || operation.IsCancellationRequested || version != _navigationVersion) return;
            if (!append) Entries.Clear();
            var known = Entries.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in page.Entries) if (known.Add(entry.Key)) Entries.Add(new(entry, NotifyChecked));
            var sorted = Entries.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Entry.LastModified)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray();
            for (int index = 0; index < sorted.Length; index++) Entries.Move(Entries.IndexOf(sorted[index]), index);
            _nextToken = page.NextToken;
            _hasConnected = true;
            NotifyChecked(); NotifyListState();
            loaded = true;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { if (!IsClosed && version == _navigationVersion) ReadError = ObjectStoragePresentation.Error(exception, "请检查网络和 RAM 权限，确认可以列出此 Bucket 的对象。"); }
        finally
        {
            if (ReferenceEquals(_listOperation, operation)) _listOperation = null;
            if (!IsClosed && version == _navigationVersion)
            {
                IsLoading = false;
                if (loaded) NavigationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>重新读取当前查询，首次容器失败则重新初始化。</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (IsClosed) return;
        if (!IsInitialized || Buckets.Count == 0) { _initializeTask = null; await ReadBucketsAsync(cancellationToken); }
        else
        {
            _listOperation?.Cancel(); ++_navigationVersion;
            SelectedEntry = null; Entries.Clear(); NotifyChecked();
            await LoadPageAsync(false, cancellationToken);
        }
    }

    /// <summary>对象行与详情操作统一解析目标。</summary>
    private ObjectStorageEntryViewModel? Target(object? parameter) => parameter as ObjectStorageEntryViewModel ?? SelectedEntry;
    /// <summary>路径段使用实际 Bucket 和完整前缀，避免字符串猜测父路径。</summary>
    private void RebuildBreadcrumbs()
    {
        Breadcrumbs.Clear();
        if (SelectedBucket is not { } bucket) return;
        Breadcrumbs.Add(new(bucket.Name, "", () => NavigateAsync(bucket, "")));
        string prefix = "";
        foreach (string part in Prefix.TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            prefix += part + "/";
            string target = prefix;
            Breadcrumbs.Add(new(part, target, () => NavigateAsync(bucket, target), target == Prefix));
        }
        OnPropertyChanged(nameof(DirectoryBreadcrumbs));
    }
    /// <summary>刷新勾选统计与批量操作条件。</summary>
    private void NotifyChecked()
    {
        foreach (string property in new[] { nameof(CheckedCount), nameof(HasCheckedEntries), nameof(IsAllChecked), nameof(ListSummary) }) OnPropertyChanged(property);
        RefreshCommands();
    }
    /// <summary>刷新错误、加载及空目录状态。</summary>
    private void NotifyListState()
    {
        foreach (string property in new[] { nameof(Status), nameof(StatusTone), nameof(HasMore), nameof(ShowState), nameof(ShowEmptyActions), nameof(ShowRecovery), nameof(StateTitle), nameof(StateDescription), nameof(ListSummary) }) OnPropertyChanged(property);
        RefreshCommands();
    }
    /// <summary>业务条件变化后同步所有操作入口。</summary>
    private void RefreshCommands()
    {
        foreach (var command in new[] { RefreshCommand, LoadMoreCommand, UploadCommand, NewFolderCommand, DownloadCommand, BatchDownloadCommand, DeleteCommand, TemporaryLinkCommand, EditConnectionCommand, ClearSearchCommand }) command.NotifyCanExecuteChanged();
        CopyKeyCommand.NotifyCanExecuteChanged(); CopyPreviewCommand.NotifyCanExecuteChanged();
    }
    /// <summary>关闭取消所有读取和传输，过期响应不会更新已释放页面。</summary>
    protected override async ValueTask ReleaseAsync()
    {
        _lifetime.Cancel(); _bucketOperation?.Cancel(); _listOperation?.Cancel(); _detailOperation?.Cancel(); _searchOperation?.Cancel();
        foreach (var transfer in Transfers) { transfer.Finished -= OnTransferFinished; transfer.Dispose(); }
        RefreshCommands();
        await Task.WhenAll(Transfers.Select(transfer => transfer.Completion));
        _transferGate.Dispose();
        _lifetime.Dispose();
    }

    /// <summary>刷新本目录或重试容器读取。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>继续请求同一查询的下一批对象。</summary>
    public AsyncRelayCommand LoadMoreCommand { get; }
    /// <summary>选择文件和处理同名策略。</summary>
    public AsyncRelayCommand UploadCommand { get; }
    /// <summary>创建当前目录下的占位文件夹。</summary>
    public AsyncRelayCommand NewFolderCommand { get; }
    /// <summary>下载当前或指定对象。</summary>
    public AsyncRelayCommand DownloadCommand { get; }
    /// <summary>下载所有勾选对象及目录。</summary>
    public AsyncRelayCommand BatchDownloadCommand { get; }
    /// <summary>先枚举再确认删除的完整范围。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
    /// <summary>生成有限期只读签名 URL。</summary>
    public AsyncRelayCommand TemporaryLinkCommand { get; }
    /// <summary>复制对象键。</summary>
    public RelayCommand CopyKeyCommand { get; }
    /// <summary>复制只读文本预览。</summary>
    public RelayCommand CopyPreviewCommand { get; }
    /// <summary>清空当前页的勾选。</summary>
    public RelayCommand ClearSelectionCommand { get; }
    /// <summary>关闭当前对象详情。</summary>
    public RelayCommand CloseDetailsCommand { get; }
    /// <summary>关闭操作失败提示。</summary>
    public RelayCommand ClearErrorCommand { get; }
    /// <summary>清空当前目录搜索。</summary>
    public AsyncRelayCommand ClearSearchCommand { get; }
    /// <summary>编辑已有连接，成功后重新读取真实容器。</summary>
    public AsyncRelayCommand EditConnectionCommand { get; }
}
