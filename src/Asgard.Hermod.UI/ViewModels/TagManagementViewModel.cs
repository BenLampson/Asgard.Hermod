namespace Asgard.Hermod.UI.ViewModels;

/// <summary>组合固定环境、可搜索自定义树与选择详情，所有写入由统一服务执行。</summary>
public sealed class TagManagementViewModel : ObservableObject, IDisposable
{
    private readonly ITagManagementService _service;
    private readonly ITagDialogService _dialogs;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly List<ManagedTagNodeViewModel> _roots = [];
    private readonly List<ManagedTagNodeViewModel> _nodes = [];
    private ManagedTagNodeViewModel? _selectedNode;
    private string _searchText = "";
    private string _error = "";
    private bool _isLoading;
    private bool _isDialogOpen;
    private bool _isInitialized;
    private bool _isDisposed;

    /// <summary>管理窗口与其他入口共享同一数据源，通过事件获取后台最新标签。</summary>
    public TagManagementViewModel(ITagManagementService service, ITagDialogService dialogs)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(dialogs);
        _service = service;
        _dialogs = dialogs;
        NewTagCommand = new AsyncRelayCommand(_ => EditTagAsync(null, null), _ => CanManage);
        EditCommand = new AsyncRelayCommand(parameter => EditTagAsync(Target(parameter), null), CanManageTarget);
        RenameCommand = new AsyncRelayCommand(parameter => EditTagAsync(Target(parameter), null, true), CanManageTarget);
        AddChildCommand = new AsyncRelayCommand(parameter => EditTagAsync(null, Target(parameter)?.Id), CanManageTarget);
        DeleteCommand = new AsyncRelayCommand(DeleteTagAsync, CanManageTarget);
        RetryCommand = new AsyncRelayCommand(_ => ReloadAsync(), _ => !_isDisposed && !IsLoading);
        ClearSearchCommand = new RelayCommand(_ => SearchText = "");
        _service.TagsChanged += OnTagsChanged;
    }

    /// <summary>固定环境来自服务，仅展示且不进入自定义编辑树。</summary>
    public ObservableCollection<ManagedTag> BuiltInTags { get; } = [];
    /// <summary>搜索和展开规则计算出的可见自定义行。</summary>
    public ObservableCollection<ManagedTagNodeViewModel> Rows { get; } = [];
    /// <summary>完整自定义数量，不随搜索变化。</summary>
    public int CustomTagCount => _nodes.Count;
    /// <summary>当前选中节点，刷新后按稳定标识恢复选择。</summary>
    public ManagedTagNodeViewModel? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (value?.Tag.IsBuiltIn == true || !SetProperty(ref _selectedNode, value)) return;
            foreach (var node in _nodes) node.IsSelected = ReferenceEquals(node, value);
            OnPropertyChanged(nameof(SelectedTag));
            OnPropertyChanged(nameof(SelectedPath));
            OnPropertyChanged(nameof(SelectedInstanceLabel));
            OnPropertyChanged(nameof(HasSelection));
            RefreshCommands();
        }
    }
    /// <summary>详情中的真实标签快照。</summary>
    public ManagedTag? SelectedTag => SelectedNode?.Tag;
    /// <summary>完整路径用间隔斜杠显示。</summary>
    public string SelectedPath => SelectedTag?.Path.Replace("/", " / ", StringComparison.Ordinal) ?? "";
    /// <summary>详情的关联数量。</summary>
    public string SelectedInstanceLabel => Text.Format("Instances", SelectedTag?.InstanceCount ?? 0);
    /// <summary>只有选择自定义标签才显示编辑操作。</summary>
    public bool HasSelection => SelectedTag is not null;
    /// <summary>仅搜索自定义标签，内置环境不受影响。</summary>
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) RebuildRows(); }
    }
    /// <summary>首次读取和后台刷新失败的消息。</summary>
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>读取期间禁用写入口，避免使用过期的父级快照。</summary>
    public bool IsLoading
    {
        get => _isLoading;
        private set { if (SetProperty(ref _isLoading, value)) { RefreshCommands(); RefreshEmptyState(); } }
    }
    /// <summary>完成首次读取，不以空结果代替读取失败。</summary>
    public bool IsInitialized { get => _isInitialized; private set => SetProperty(ref _isInitialized, value); }
    /// <summary>尚未创建自定义标签的真实空态。</summary>
    public bool IsEmpty => IsInitialized && !IsLoading && CustomTagCount == 0 && SearchText.Trim().Length == 0;
    /// <summary>搜索无匹配时显示清空入口，保留固定环境和当前详情。</summary>
    public bool HasNoMatches => IsInitialized && !IsLoading && SearchText.Trim().Length > 0 && Rows.Count == 0;
    /// <summary>统一的写操作启用条件。</summary>
    public bool CanManage => !_isDisposed && IsInitialized && !IsLoading && !_isDialogOpen;
    /// <summary>创建根级标签。</summary>
    public AsyncRelayCommand NewTagCommand { get; }
    /// <summary>编辑所选标签或上下文菜单目标。</summary>
    public AsyncRelayCommand EditCommand { get; }
    /// <summary>只重命名所选标签，不展示父级或颜色设置。</summary>
    public AsyncRelayCommand RenameCommand { get; }
    /// <summary>添加子级并预填父级。</summary>
    public AsyncRelayCommand AddChildCommand { get; }
    /// <summary>打开完整删除预览。</summary>
    public AsyncRelayCommand DeleteCommand { get; }
    /// <summary>后台读取失败时重试。</summary>
    public AsyncRelayCommand RetryCommand { get; }
    /// <summary>恢复完整自定义树。</summary>
    public RelayCommand ClearSearchCommand { get; }

    /// <summary>可在宿主显示窗体前读取一次，窗口加载不会重复执行成功的初始化。</summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        IsInitialized ? Task.CompletedTask : ReloadAsync(cancellationToken: cancellationToken);

    /// <summary>串行读取最新快照，并恢复选择与非搜索展开状态。</summary>
    public async Task ReloadAsync(string? selectedTagId = null, CancellationToken cancellationToken = default)
    {
        if (_isDisposed) return;
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        var entered = false;
        try
        {
            await _reloadGate.WaitAsync(operation.Token);
            entered = true;
            if (_isDisposed) return;
            IsLoading = true;
            Error = "";
            var tags = await _service.GetTagsAsync(operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            var selectionId = selectedTagId ?? SelectedNode?.Id;
            var expanded = _nodes.ToDictionary(node => node.Id, node => node.IsExpanded, StringComparer.Ordinal);
            BuiltInTags.Clear();
            foreach (var tag in tags.Where(tag => tag.IsBuiltIn).OrderBy(EnvironmentOrder))
                BuiltInTags.Add(tag);
            BuildTree(tags.Where(tag => !tag.IsBuiltIn).ToArray());
            foreach (var node in _nodes)
                if (expanded.TryGetValue(node.Id, out var wasExpanded)) node.IsExpanded = wasExpanded;
            IsInitialized = true;
            RebuildRows();
            SelectedNode = _nodes.FirstOrDefault(node => node.Id == selectionId);
            OnPropertyChanged(nameof(CustomTagCount));
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception exception) { if (!_isDisposed) Error = Text.Error(exception); }
        finally
        {
            if (entered)
            {
                if (!_isDisposed) IsLoading = false;
                _reloadGate.Release();
            }
        }
    }

    /// <summary>使用稳定父子标识生成树；异常孤儿节点仍可见，避免静默丢失快照。</summary>
    private void BuildTree(IReadOnlyList<ManagedTag> tags)
    {
        _roots.Clear();
        _nodes.Clear();
        var known = tags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        ManagedTagNodeViewModel Build(ManagedTag tag, int depth)
        {
            visited.Add(tag.Id);
            var node = new ManagedTagNodeViewModel(tag, depth, this, RebuildRows);
            _nodes.Add(node);
            foreach (var child in tags.Where(candidate => candidate.ParentId == tag.Id)
                         .OrderBy(candidate => candidate.Name, StringComparer.OrdinalIgnoreCase))
                if (!visited.Contains(child.Id)) node.Children.Add(Build(child, depth + 1));
            return node;
        }
        foreach (var tag in tags.OrderBy(tag => tag.Path, StringComparer.OrdinalIgnoreCase))
            if ((tag.ParentId is null || !known.Contains(tag.ParentId)) && !visited.Contains(tag.Id))
                _roots.Add(Build(tag, 0));
        foreach (var tag in tags)
            if (!visited.Contains(tag.Id)) _roots.Add(Build(tag, 0));
    }

    /// <summary>搜索自动显示命中的祖先和子级，不改动用户原来的折叠状态或选择。</summary>
    private void RebuildRows()
    {
        if (_isDisposed) return;
        var query = SearchText.Trim();
        bool Matches(ManagedTagNodeViewModel node) => node.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || node.Tag.Path.Contains(query, StringComparison.OrdinalIgnoreCase) || node.Children.Any(Matches);
        void Add(ManagedTagNodeViewModel node)
        {
            if (query.Length > 0 && !Matches(node)) return;
            Rows.Add(node);
            if (query.Length > 0 || node.IsExpanded)
                foreach (var child in node.Children) Add(child);
        }
        Rows.Clear();
        foreach (var node in _roots) Add(node);
        RefreshEmptyState();
    }

    /// <summary>只有自定义标签可以成为编辑、添加子级和删除的目标。</summary>
    private ManagedTag? Target(object? parameter) => parameter switch
    {
        ManagedTagNodeViewModel node => node.Tag,
        ManagedTag tag => tag,
        _ => SelectedTag,
    };
    /// <summary>列表菜单与详情按钮共用同一启用条件。</summary>
    private bool CanManageTarget(object? parameter) => CanManage && Target(parameter) is { IsBuiltIn: false };

    /// <summary>编辑服务成功后读取真实结果并选中新标签，取消时保留原选择。</summary>
    private async Task EditTagAsync(ManagedTag? tag, string? parentId, bool renameOnly = false)
    {
        _isDialogOpen = true;
        RefreshCommands();
        try
        {
            var result = renameOnly && tag is not null ? await _dialogs.RenameAsync(tag, _lifetime.Token)
                : await _dialogs.EditAsync(tag, parentId, _lifetime.Token);
            if (result is not null && !_isDisposed)
            {
                SearchText = "";
                await ReloadAsync(result.Id);
            }
        }
        catch (OperationCanceledException) when (_isDisposed) { }
        catch (Exception exception) { if (!_isDisposed) Error = Text.Error(exception); }
        finally { _isDialogOpen = false; RefreshCommands(); }
    }

    /// <summary>确认服务成功后清除已删除选择，实例关联刷新由统一服务事件驱动。</summary>
    private async Task DeleteTagAsync(object? parameter)
    {
        var tag = Target(parameter);
        if (tag is null) return;
        _isDialogOpen = true;
        RefreshCommands();
        try
        {
            if (await _dialogs.DeleteAsync(tag, _lifetime.Token) && !_isDisposed)
                await ReloadAsync();
        }
        catch (OperationCanceledException) when (_isDisposed) { }
        catch (Exception exception) { if (!_isDisposed) Error = Text.Error(exception); }
        finally { _isDialogOpen = false; RefreshCommands(); }
    }

    /// <summary>后台事件切回创建状态时的界面上下文，关闭后不再触发读取。</summary>
    private void OnTagsChanged(object? sender, EventArgs args)
    {
        if (_isDisposed) return;
        if (_context is not null && SynchronizationContext.Current != _context)
            _context.Post(_ => { if (!_isDisposed) _ = ReloadAsync(); }, null);
        else _ = ReloadAsync();
    }

    /// <summary>环境标签按设计稿的固定顺序排列。</summary>
    private static int EnvironmentOrder(ManagedTag tag) => tag.Name switch
    {
        "Production" => 0,
        "Staging" => 1,
        "Development" => 2,
        _ => 3,
    };
    /// <summary>统一刷新动作按钮。</summary>
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanManage));
        NewTagCommand.NotifyCanExecuteChanged();
        EditCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        AddChildCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }
    /// <summary>读取、搜索和空数据变化时刷新边界状态。</summary>
    private void RefreshEmptyState()
    {
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasNoMatches));
    }

    /// <summary>解除后台事件并取消未完成读取，禁止已关闭窗口继续更新。</summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _service.TagsChanged -= OnTagsChanged;
        _lifetime.Cancel();
        _lifetime.Dispose();
        RefreshCommands();
    }
}
