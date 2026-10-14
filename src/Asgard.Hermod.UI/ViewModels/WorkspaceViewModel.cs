namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理可切换工作页，保证页签关闭和应用退出时释放终端。</summary>
public sealed partial class WorkspaceViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IWorkspaceDataSource _source;
    private readonly ITerminalSessionFactory _terminalFactory;
    private readonly IInstanceWorkspaceFactory? _pageFactory;
    private WorkspacePageViewModel? _selectedTab;
    private Task? _disposeTask;
    private bool _disposeStarted;
    private int _terminalNumber;
    private bool _useIndependentWindows;
    private readonly HashSet<Task> _closingPages = [];
    private bool _isExitPending;
    private readonly Dictionary<WorkspacePageViewModel, InstanceItemViewModel> _instances = [];

    /// <summary>启动时仅打开首页，实例页签由用户主动打开。</summary>
    public WorkspaceViewModel(IWorkspaceDataSource source, ITerminalSessionFactory terminalFactory,
        IInstanceWorkspaceFactory? pageFactory = null, IInstanceProviderRegistry? registry = null,
        IRecentInstanceStore? recentInstances = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(terminalFactory);
        _source = source;
        _terminalFactory = terminalFactory;
        _pageFactory = pageFactory;
        Home = new HomeWorkspaceViewModel(OpenInstance, registry, recentInstances);
        Home.Refresh(source.GetInstances().Select(instance => new InstanceItemViewModel(instance)).ToArray());
        _selectedTab = Home;
        _selectedTab.IsSelected = true;
        Tabs.Add(_selectedTab);
        SelectTabCommand = new RelayCommand(value =>
        {
            if (value is WorkspacePageViewModel page && Tabs.Contains(page) && !page.IsClosed)
                SelectedTab = page;
        });
        CloseTabCommand = new AsyncRelayCommand(value =>
            value is WorkspacePageViewModel page ? CloseTabAsync(page) : Task.CompletedTask,
            value => !_disposeStarted && value is WorkspacePageViewModel { CanClose: true, IsClosed: false } page && Tabs.Contains(page));
    }

    /// <summary>活动页变化后同步侧栏状态。</summary>
    public event EventHandler? SelectedTabChanged;
    /// <summary>宿主收到打开请求后创建或唤回对应的独立窗口。</summary>
    public event Action<WorkspacePageViewModel>? PageActivated;
    /// <summary>桌面宿主启用独立窗口；嵌入式消费者仍可复用原有页签。</summary>
    public bool UseIndependentWindows
    {
        get => _useIndependentWindows;
        set
        {
            if (!SetProperty(ref _useIndependentWindows, value)) return;
            OnPropertyChanged(nameof(DisplayedPage));
            OnPropertyChanged(nameof(TabStripVisibility));
        }
    }
    /// <summary>独立窗口模式下主窗体始终保留首页。</summary>
    public WorkspacePageViewModel? DisplayedPage => UseIndependentWindows ? Home : SelectedTab;
    /// <summary>退出预检期间禁止打开或关闭其他页，保持受检窗口快照稳定。</summary>
    public bool IsExitPending { get => _isExitPending; set => _isExitPending = value; }
    /// <summary>包括已移出页签集合但仍在异步释放的工作页。</summary>
    public int PendingCloseCount => _closingPages.Count;
    /// <summary>独立窗口不在主窗体重复呈现外层页签。</summary>
    public Visibility TabStripVisibility => UseIndependentWindows ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>按显示顺序排列的页签。</summary>
    public ObservableCollection<WorkspacePageViewModel> Tabs { get; } = [];
    /// <summary>首页负责统计与本机最近访问，关闭页签后仍维护访问记录。</summary>
    public HomeWorkspaceViewModel Home { get; }
    /// <summary>外壳提供同一个目录元数据命令，供独立窗口继承。</summary>
    public ICommand? RenameInstanceCommand { get; internal set; }
    /// <summary>已打开会话按稳定实例标识刷新标题，不重建页面或重新连接。</summary>
    public void RefreshInstanceMetadata(IReadOnlyList<InstanceItemViewModel> instances)
    {
        foreach (var page in Tabs)
        {
            var current = instances.FirstOrDefault(instance => instance.Id == page.InstanceId);
            if (current is null) continue;
            if (_instances.TryGetValue(page, out var existing) && !ReferenceEquals(current, existing))
                existing.UpdateSnapshot(current.Summary);
            page.UpdateInstanceMetadata(current.Summary);
        }
        Home.Refresh(instances);
    }
    /// <summary>当前工作页；全部页签关闭后为空。</summary>
    public WorkspacePageViewModel? SelectedTab
    {
        get => _selectedTab;
        set
        {
            if (ReferenceEquals(_selectedTab, value) || value is not null && (!Tabs.Contains(value) || value.IsClosed))
                return;
            _selectedTab?.IsSelected = false;
            _selectedTab = value;
            _selectedTab?.IsSelected = true;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayedPage));
            SelectedTabChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    /// <summary>切换页签。</summary>
    public RelayCommand SelectTabCommand { get; }
    /// <summary>关闭页签并等待资源释放。</summary>
    public AsyncRelayCommand CloseTabCommand { get; }

    /// <summary>已有实例页优先复用；本地实例才创建真实终端。</summary>
    public void OpenInstance(InstanceItemViewModel instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (IsExitPending) return;
        ObjectDisposedException.ThrowIf(_disposeStarted, this);
        WorkspacePageViewModel? page = Tabs.FirstOrDefault(tab => tab.InstanceId == instance.Id && !tab.IsClosed);
        if (page is null)
        {
            page = instance.IsLocal ? CreateTerminalPage(instance) : CreateServicePage(instance);
            Tabs.Add(page);
            RegisterSessionPage(page, instance);
        }
        SelectedTab = page;
        ActivatePage(page);
        Home.RecordOpened(instance);
    }

    /// <summary>用户主动请求时创建一个新的本地终端页。</summary>
    public void OpenLocalTerminal()
    {
        if (IsExitPending) return;
        ObjectDisposedException.ThrowIf(_disposeStarted, this);
        var local = new InstanceItemViewModel(_source.GetInstances().FirstOrDefault(instance => instance.IsLocal)
            ?? new InstanceSummary("local-shell", "sandbox-local", Text.Get("LocalTerminal"), "laptop", ["Local"], true));
        var page = CreateTerminalPage(local);
        Tabs.Add(page);
        RegisterSessionPage(page, local);
        SelectedTab = page;
        ActivatePage(page);
        Home.RecordOpened(local);
    }

    /// <summary>关闭活动页时选择邻近页，最后一个页签关闭后清空内容。</summary>
    public async Task CloseTabAsync(WorkspacePageViewModel page)
    {
        if (IsExitPending || !page.CanClose || !Tabs.Contains(page))
            return;
        if (!await page.RequestCloseAsync() || !Tabs.Contains(page)) return;
        int index = Tabs.IndexOf(page);
        if (ReferenceEquals(SelectedTab, page))
            SelectedTab = Tabs.Count == 1 ? null : Tabs[index > 0 ? index - 1 : 1];
        Tabs.Remove(page);
        _initializations.Remove(page);
        page.NewSessionRequested -= OnNewSessionRequested;
        page.CloseRequested -= OnPageCloseRequested;
        _instances.Remove(page);
        Task closing = page.CloseAsync();
        _closingPages.Add(closing);
        try { await closing; }
        finally { _closingPages.Remove(closing); }
    }

    /// <summary>通过应用层读取详情快照。</summary>
    private WorkspacePageViewModel CreateServicePage(InstanceItemViewModel instance) =>
        _pageFactory?.CreatePage(instance) ?? new ServiceDetailViewModel(instance, _source.GetServiceSnapshot(instance.Id));

    /// <summary>每个新终端拥有独立页签和应用层会话句柄。</summary>
    private TerminalWorkspaceViewModel CreateTerminalPage(InstanceItemViewModel instance) =>
        new("terminal:" + ++_terminalNumber, instance, _terminalFactory);

    /// <summary>外层页签绑定自己的实例，不把功能会话混进主框架协议逻辑。</summary>
    private void RegisterSessionPage(WorkspacePageViewModel page, InstanceItemViewModel instance)
    {
        _instances[page] = instance;
        page.NewSessionRequested += OnNewSessionRequested;
        page.CloseRequested += OnPageCloseRequested;
    }
    /// <summary>新会话复用实例配置，但创建独立页面和连接。</summary>
    private void OnNewSessionRequested(object? sender, EventArgs args)
    {
        if (IsExitPending || _disposeStarted || sender is not WorkspacePageViewModel original || !_instances.TryGetValue(original, out var instance)) return;
        var page = CreateServicePage(instance); RegisterSessionPage(page, instance);
        Tabs.Add(page); SelectedTab = page; Home.RecordOpened(instance);
        ActivatePage(page);
    }

    /// <summary>用户退出应用前逐页确认，取消任一页面时保持所有会话和草稿。</summary>
    public async Task<bool> RequestCloseAllAsync()
    {
        foreach (var page in Tabs.ToArray())
            if (!page.IsClosed && !await page.RequestCloseAsync()) return false;
        return true;
    }

    /// <summary>应用退出时关闭所有页，不保存或恢复已打开的页签。</summary>
    public ValueTask DisposeAsync()
    {
        _disposeStarted = true;
        _disposeTask ??= CloseAllAsync();
        return new ValueTask(_disposeTask);
    }

    /// <summary>统一启动关闭，让所有终端的清理都得到等待。</summary>
    private async Task CloseAllAsync()
    {
        SelectedTab = null;
        foreach (var page in Tabs) { page.NewSessionRequested -= OnNewSessionRequested; page.CloseRequested -= OnPageCloseRequested; }
        _instances.Clear();
        Task[] initializing = _initializations.Values.ToArray(); _initializations.Clear();
        Task[] closing = Tabs.Select(page => page.CloseAsync()).Concat(_closingPages).ToArray();
        Tabs.Clear();
        await Task.WhenAll(closing.Concat(initializing));
    }
}
