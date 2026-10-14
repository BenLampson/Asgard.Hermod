namespace Asgard.Hermod.UI.ViewModels;

/// <summary>维护实例搜索、标签过滤和导航命令。</summary>
public sealed class InstanceSidebarViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<InstanceItemViewModel> _items;
    private readonly IInstanceEditor _editor;
    private readonly Action<InstanceItemViewModel> _openInstance;
    private readonly IWorkspaceDataSource _source;
    private readonly ITagManagementService? _tagService;
    private readonly IInstanceProviderRegistry? _registry;
    private readonly Func<string, ConfigurationSyncStatus>? _syncStatus;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private IReadOnlyList<ManagedTag> _managedTags = [];
    private long _refreshRevision;
    private bool _disposed;
    private string _operationError = "";
    private string _query = "";
    private string[] _selectedTagPaths = [];
    private TagFilterMatchMode _tagMatchMode = TagFilterMatchMode.Any;

    /// <summary>将应用层实例转换为共享导航条目。</summary>
    public InstanceSidebarViewModel(IWorkspaceDataSource source, IInstanceEditor editor,
        Action<InstanceItemViewModel> openInstance, Action openLocalTerminal, ITagManagementService? tags = null,
        IInstanceProviderRegistry? registry = null, Func<string, ConfigurationSyncStatus>? syncStatus = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(editor);
        ArgumentNullException.ThrowIfNull(openInstance);
        ArgumentNullException.ThrowIfNull(openLocalTerminal);
        _editor = editor;
        _openInstance = openInstance;
        _source = source;
        _tagService = tags;
        _registry = registry;
        _syncStatus = syncStatus;
        _items = new ObservableCollection<InstanceItemViewModel>(
            source.GetInstances().DistinctBy(instance => instance.Id).Select(CreateItem));
        Instances = new CollectionViewSource { Source = _items }.View;
        Instances.Filter = MatchesFilter;
        Instances.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
        OpenInstanceCommand = new RelayCommand(value =>
        {
            if (value is InstanceItemViewModel instance)
                _openInstance(instance);
        }, value => value is InstanceItemViewModel);
        SelectTagCommand = new RelayCommand(value => SelectedTag = value as string);
        ClearTagCommand = new RelayCommand(_ => SelectedTag = null);
        OpenLocalTerminalCommand = new RelayCommand(_ => openLocalTerminal());
        AddInstanceCommand = new AsyncRelayCommand(_ => AddInstanceAsync());
        AddInstanceCommand.ExecutionFailed += OnAddInstanceFailed;
        RenameInstanceCommand = new AsyncRelayCommand(value => EditMetadataAsync(value, false), CanEditMetadata);
        EditInstanceTagsCommand = new AsyncRelayCommand(value => EditMetadataAsync(value, true), CanEditMetadata);
        if (_tagService is not null)
            _tagService.TagsChanged += OnTagsChanged;
        RefreshTags();
    }

    /// <summary>实例增加时通知全局搜索更新。</summary>
    public event EventHandler? InstancesChanged;
    /// <summary>侧栏过滤视图；折叠导航使用同一数据源。</summary>
    public ICollectionView Instances { get; }
    /// <summary>未经侧栏过滤的全局搜索来源。</summary>
    public IReadOnlyList<InstanceItemViewModel> AllInstances => _items;
    /// <summary>可选标签列表。</summary>
    public ObservableCollection<string> AvailableTags { get; } = [];
    /// <summary>通过统一安全文案反馈刷新或创建失败。</summary>
    public string OperationError { get => _operationError; private set => SetProperty(ref _operationError, value); }
    /// <summary>实例名称过滤文字。</summary>
    public string Query
    {
        get => _query;
        set { if (SetProperty(ref _query, value)) RefreshFilter(); }
    }
    /// <summary>单标签入口保留给导航命令；设置后使用任意匹配。</summary>
    public string? SelectedTag
    {
        get => _selectedTagPaths.FirstOrDefault();
        set => ApplyTagFilter(string.IsNullOrWhiteSpace(value) ? [] : [value], TagFilterMatchMode.Any);
    }
    /// <summary>已应用的标签路径；弹出选择器单独维护草稿。</summary>
    public IReadOnlyList<string> SelectedTagPaths => Array.AsReadOnly(_selectedTagPaths);
    /// <summary>任一条件满足，或者要求每个已选路径都有匹配标签。</summary>
    public TagFilterMatchMode TagMatchMode => _tagMatchMode;
    /// <summary>过滤按钮显示文字。</summary>
    public string TagFilterLabel => _selectedTagPaths.Length switch
    {
        0 => Text.Get("TagFilter"),
        1 => Localization.TagDisplayText.Name(_selectedTagPaths[0], _selectedTagPaths[0].Split('/')[^1]),
        _ => Text.Format("Tags", _selectedTagPaths.Length)
    };
    /// <summary>是否没有匹配实例。</summary>
    public bool IsEmpty => Instances.IsEmpty;
    /// <summary>打开选中的实例。</summary>
    public RelayCommand OpenInstanceCommand { get; }
    /// <summary>选择标签。</summary>
    public RelayCommand SelectTagCommand { get; }
    /// <summary>清除标签过滤。</summary>
    public RelayCommand ClearTagCommand { get; }
    /// <summary>明确打开新的本地终端。</summary>
    public RelayCommand OpenLocalTerminalCommand { get; }
    /// <summary>收集并添加当前工作区中的实例配置。</summary>
    public AsyncRelayCommand AddInstanceCommand { get; }
    /// <summary>独立修改实例名称，不测试连接。</summary>
    public AsyncRelayCommand RenameInstanceCommand { get; }
    /// <summary>独立调整实例标签。</summary>
    public AsyncRelayCommand EditInstanceTagsCommand { get; }
    private bool _metadataDialogOpen;
    private bool CanEditMetadata(object? value) => !_disposed && !_metadataDialogOpen
        && value is InstanceItemViewModel { IsLocal: false } instance && _items.Contains(instance);
    private async Task EditMetadataAsync(object? value, bool editTags)
    {
        if (!CanEditMetadata(value) || value is not InstanceItemViewModel instance) return;
        _metadataDialogOpen = true; RefreshMetadataCommands();
        try
        {
            var result = editTags ? await _editor.EditTagsAsync(instance.Id) : await _editor.RenameAsync(instance.Id);
            if (result is null || _disposed) return;
            Interlocked.Increment(ref _refreshRevision);
            var current = _items.FirstOrDefault(item => item.Id == instance.Id);
            current?.UpdateSnapshot(result);
            RefreshTags(); RefreshFilter(); RefreshSyncStates(); OperationError = "";
            InstancesChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception error) { if (!_disposed) OperationError = Text.Error(error); }
        finally { _metadataDialogOpen = false; RefreshMetadataCommands(); }
    }
    private void RefreshMetadataCommands()
    {
        RenameInstanceCommand.NotifyCanExecuteChanged(); EditInstanceTagsCommand.NotifyCanExecuteChanged();
    }
    /// <summary>同步导航选中态，不受当前过滤条件影响。</summary>
    public void SelectInstance(string? instanceId)
    {
        foreach (var instance in _items)
            instance.IsSelected = instance.Id == instanceId;
    }

    /// <summary>使用持久化标签目录建立树，尚未关联实例的自定义标签也立即可选。</summary>
    public IReadOnlyList<TagFilterDefinition> BuildTagFilterDefinitions() =>
        TagFilterTreeBuilder.Create(_managedTags.Select(tag => tag.Path).Concat(_items.SelectMany(instance => instance.Tags)),
            _managedTags.ToDictionary(tag => tag.Path, tag => tag.Color, StringComparer.OrdinalIgnoreCase));

    /// <summary>启动和标签修改后重新读取快照，以稳定标识保留条目与筛选关联。</summary>
    public async Task RefreshAsync()
    {
        if (_disposed)
            return;
        long revision = Interlocked.Increment(ref _refreshRevision);
        var tags = _tagService is null ? _managedTags : await _tagService.GetTagsAsync();
        var summaries = await Task.Run(_source.GetInstances);
        if (_disposed || revision != _refreshRevision)
            return;
        string[] selectedIds = _managedTags.Where(tag => _selectedTagPaths.Contains(tag.Path,
            StringComparer.OrdinalIgnoreCase)).Select(tag => tag.Id).ToArray();
        var oldCatalog = _managedTags;
        _managedTags = tags;
        // 改名、移动和删除只变更路径，过滤通过稳定标签标识跟随修改。
        string[] selectedPaths = _selectedTagPaths.Where(path => !oldCatalog.Any(tag =>
            tag.Path.Equals(path, StringComparison.OrdinalIgnoreCase))).Concat(tags
            .Where(tag => selectedIds.Contains(tag.Id)).Select(tag => tag.Path)).ToArray();
        var existingIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in _items.ToArray())
        {
            var summary = summaries.FirstOrDefault(value => value.Id == item.Id);
            if (summary is null || !existingIds.Add(item.Id))
                _items.Remove(item);
            else
                item.UpdateSnapshot(summary);
        }
        foreach (var summary in summaries.Where(value => _items.All(item => item.Id != value.Id)))
            _items.Add(CreateItem(summary));
        RefreshSyncStates();
        ApplyTagFilter(selectedPaths, TagMatchMode);
        RefreshTags();
        RefreshFilter();
        OperationError = "";
        InstancesChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>后台持久化通知统一回到所属 Dispatcher，不在数据库线程修改 WPF 集合。</summary>
    private async void OnTagsChanged(object? sender, EventArgs args)
    {
        if (_disposed || _dispatcher.HasShutdownStarted)
            return;
        try { await _dispatcher.InvokeAsync(RefreshAsync).Task.Unwrap(); }
        catch (Exception)
        {
            if (!_disposed && !_dispatcher.HasShutdownStarted)
                await _dispatcher.InvokeAsync(() => OperationError = Text.Get("CannotRefreshTagsReopenSettingsAndTryAgain"));
        }
    }

    /// <summary>创建服务异常不直接暴露第三方请求或凭据。</summary>
    private void OnAddInstanceFailed(object? sender, Exception exception) =>
        OperationError = Text.Get("CannotOpenInstanceConfigurationTryAgain");

    /// <summary>只有用户应用草稿后才更新实例列表；父路径包含全部下级标签。</summary>
    public void ApplyTagFilter(IReadOnlyList<string> selectedPaths, TagFilterMatchMode matchMode)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        if (!Enum.IsDefined(matchMode))
            throw new ArgumentOutOfRangeException(nameof(matchMode));
        string[] paths = selectedPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(TagFilterTreeBuilder.NormalizePath).Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (_selectedTagPaths.SequenceEqual(paths, StringComparer.OrdinalIgnoreCase)
            && _tagMatchMode == matchMode)
            return;
        _selectedTagPaths = paths;
        _tagMatchMode = matchMode;
        OnPropertyChanged(nameof(SelectedTagPaths));
        OnPropertyChanged(nameof(SelectedTag));
        OnPropertyChanged(nameof(TagMatchMode));
        OnPropertyChanged(nameof(TagFilterLabel));
        RefreshFilter();
    }

    /// <summary>名称与标签条件同时满足时显示该实例。</summary>
    private bool MatchesFilter(object value)
    {
        if (value is not InstanceItemViewModel instance)
            return false;
        bool matchesTag = _selectedTagPaths.Length == 0 || (_tagMatchMode == TagFilterMatchMode.Any
            ? _selectedTagPaths.Any(path => MatchesTagPath(instance, path))
            : _selectedTagPaths.All(path => MatchesTagPath(instance, path)));
        return matchesTag && MatchesSearch(instance, Query);
    }

    /// <summary>分隔符限定父子边界，避免 Production 误匹配 Production-copy。</summary>
    private static bool MatchesTagPath(InstanceItemViewModel instance, string path) => instance.Tags.Any(tag =>
    {
        string normalized = TagFilterTreeBuilder.NormalizePath(tag);
        return normalized.Equals(path, StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith(path + "/", StringComparison.OrdinalIgnoreCase);
    });

    /// <summary>统一侧栏和全局搜索的名称匹配规则。</summary>
    internal static bool MatchesSearch(InstanceItemViewModel instance, string query)
    {
        string search = query.Trim();
        return search.Length == 0 || instance.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || instance.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || instance.Tags.Any(tag => tag.Contains(search, StringComparison.OrdinalIgnoreCase)
                || Localization.TagDisplayText.FullPath(tag).Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>重新应用过滤并刷新空状态。</summary>
    private void RefreshFilter()
    {
        Instances.Refresh();
        OnPropertyChanged(nameof(IsEmpty));
    }

    /// <summary>增加实例后更新可选标签。</summary>
    private void RefreshTags()
    {
        AvailableTags.Clear();
        foreach (string tag in _managedTags.Select(tag => tag.Path).Concat(_items.SelectMany(item => item.Tags))
            .Select(TagFilterTreeBuilder.NormalizePath)
            .Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase))
            AvailableTags.Add(tag);
    }

    /// <summary>宿主成功保存后加入导航；取消或失败不改变工作区。</summary>
    private async Task AddInstanceAsync()
    {
        InstanceSummary? created = await _editor.CreateAsync();
        if (created is null || _disposed)
            return;
        OperationError = "";
        // 保存通知可能已经刷新并插入该实例；按稳定 ID 更新，且淘汰保存前启动的旧刷新。
        Interlocked.Increment(ref _refreshRevision);
        var instance = _items.FirstOrDefault(item => item.Id == created.Id);
        if (instance is null) { instance = CreateItem(created); _items.Add(instance); }
        else instance.UpdateSnapshot(created);
        RefreshSyncStates();
        Query = "";
        SelectedTag = null;
        RefreshTags();
        RefreshFilter();
        InstancesChanged?.Invoke(this, EventArgs.Empty);
        _openInstance(instance);
    }

    /// <summary>所有创建路径复用类型元数据和当前账户同步状态。</summary>
    private InstanceItemViewModel CreateItem(InstanceSummary summary)
    {
        var item = new InstanceItemViewModel(summary, _registry);
        item.UpdateSyncStatus(_syncStatus?.Invoke(summary.Id) ?? ConfigurationSyncStatus.Local);
        return item;
    }

    /// <summary>后台状态变化只更新标记，不需要重新读取实例或重建集合。</summary>
    public void RefreshSyncStates()
    {
        foreach (var item in _items)
            item.UpdateSyncStatus(_syncStatus?.Invoke(item.Id) ?? ConfigurationSyncStatus.Local);
    }

    /// <summary>关闭外壳时释放目录订阅，不让持久化事件继续访问已销毁视图。</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_tagService is not null)
            _tagService.TagsChanged -= OnTagsChanged;
        AddInstanceCommand.ExecutionFailed -= OnAddInstanceFailed;
    }
}
