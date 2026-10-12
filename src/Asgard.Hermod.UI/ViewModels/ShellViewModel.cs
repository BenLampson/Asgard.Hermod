using Asgard.Hermod.UI.Themes;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>协调外壳、导航与工作区，主题和连接通过宿主服务接入。</summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IThemeService _themes;
    private readonly IAccountService? _accounts;
    private readonly HashSet<WorkspacePageViewModel> _instancePages = [];
    private bool _isSidebarExpanded = true;
    private string _globalSearchQuery = "";
    private bool _disposed;

    /// <summary>装配外壳，构造时不启动 Shell 或远程连接。</summary>
    public ShellViewModel(IWorkspaceDataSource source, IInstanceEditor editor,
        ITerminalSessionFactory terminalFactory, IThemeService themes,
        ISettingsEditor? settings = null, ITagManagementService? tags = null,
        IInstanceWorkspaceFactory? pageFactory = null, IAccountService? accounts = null, IAccountEditor? accountEditor = null)
    {
        ArgumentNullException.ThrowIfNull(themes);
        _themes = themes;
        _accounts = accounts;
        Workspace = new WorkspaceViewModel(source, terminalFactory, pageFactory);
        Sidebar = new InstanceSidebarViewModel(source, editor, Workspace.OpenInstance, Workspace.OpenLocalTerminal, tags);
        OpenSettingsCommand = new AsyncRelayCommand(_ => settings?.OpenAsync() ?? Task.CompletedTask,
            _ => settings is not null);
        OpenAccountCommand = new AsyncRelayCommand(_ => accountEditor?.OpenAsync() ?? Task.CompletedTask,
            _ => accountEditor is not null);
        if (accounts is not null) accounts.AccountChanged += OnAccountChanged;
        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);
        SetLightThemeCommand = new RelayCommand(_ => _themes.SetTheme(UiTheme.Light));
        SetDarkThemeCommand = new RelayCommand(_ => _themes.SetTheme(UiTheme.Dark));
        ToggleThemeCommand = new RelayCommand(_ => _themes.SetTheme(IsDarkTheme ? UiTheme.Light : UiTheme.Dark));
        OpenLocalTerminalCommand = new RelayCommand(_ => Workspace.OpenLocalTerminal());
        OpenGlobalSearchResultCommand = new RelayCommand(value =>
        {
            if (value is InstanceItemViewModel instance)
            {
                Workspace.OpenInstance(instance);
                GlobalSearchQuery = "";
            }
        }, value => value is InstanceItemViewModel);
        OpenFirstSearchResultCommand = new RelayCommand(_ =>
        {
            if (GlobalSearchResults.FirstOrDefault() is { } first)
                OpenGlobalSearchResultCommand.Execute(first);
        }, _ => GlobalSearchResults.Count > 0);
        _themes.ThemeChanged += OnThemeChanged;
        Sidebar.InstancesChanged += OnInstancesChanged;
        Workspace.SelectedTabChanged += OnSelectedTabChanged;
    }

    /// <summary>实例导航状态。</summary>
    public InstanceSidebarViewModel Sidebar { get; }
    /// <summary>工作页状态。</summary>
    public WorkspaceViewModel Workspace { get; }
    /// <summary>当前使用设备上的用户级工作区。</summary>
    public string WorkspaceName => Text.Get("LocalWorkspace");
    /// <summary>登录后显示已验证 JWT 用户名，否则显示本地模式。</summary>
    public string AccountName => _accounts?.Account?.Username ?? Text.Get("Local");
    /// <summary>当前是否持有经过验证的用户会话。</summary>
    public bool IsSignedIn => _accounts?.Account is not null;
    /// <summary>账号入口随登录状态更新，无原始异常信息。</summary>
    public string AccountStatus => Text.Get(IsSignedIn ? "SignedIn" : "NotSignedIn");
    /// <summary>登录恢复失败仍允许离线使用本地工作区。</summary>
    public string AccountMessage { get; private set; } = "";
    /// <summary>当前程序集版本。</summary>
    public string Version => "v" + CompactVersion;
    /// <summary>收起侧栏使用紧凑版本文本。</summary>
    public string CompactVersion => typeof(ShellViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
    /// <summary>是否显示完整侧栏。</summary>
    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set { if (SetProperty(ref _isSidebarExpanded, value)) OnPropertyChanged(nameof(SidebarWidth)); }
    }
    /// <summary>设计中的展开与收起宽度。</summary>
    public GridLength SidebarWidth => new(IsSidebarExpanded ? 240 : 64);
    /// <summary>当前是否为暗色主题。</summary>
    public bool IsDarkTheme => _themes.Theme == UiTheme.Dark;
    /// <summary>当前是否为亮色主题。</summary>
    public bool IsLightTheme => !IsDarkTheme;
    /// <summary>全局实例搜索文字。</summary>
    public string GlobalSearchQuery
    {
        get => _globalSearchQuery;
        set { if (SetProperty(ref _globalSearchQuery, value)) RefreshGlobalSearch(); }
    }
    /// <summary>全局搜索独立于侧栏的标签过滤。</summary>
    public ObservableCollection<InstanceItemViewModel> GlobalSearchResults { get; } = [];
    /// <summary>切换侧栏状态。</summary>
    public RelayCommand ToggleSidebarCommand { get; }
    /// <summary>选择亮色主题。</summary>
    public RelayCommand SetLightThemeCommand { get; }
    /// <summary>选择暗色主题。</summary>
    public RelayCommand SetDarkThemeCommand { get; }
    /// <summary>收起侧栏中的单按钮主题切换。</summary>
    public RelayCommand ToggleThemeCommand { get; }
    /// <summary>打开新的本地终端。</summary>
    public RelayCommand OpenLocalTerminalCommand { get; }
    /// <summary>打开系统设置，共用账号菜单和标题栏入口。</summary>
    public AsyncRelayCommand OpenSettingsCommand { get; }
    /// <summary>打开登录与同步窗口。</summary>
    public AsyncRelayCommand OpenAccountCommand { get; }
    /// <summary>打开指定搜索结果。</summary>
    public RelayCommand OpenGlobalSearchResultCommand { get; }
    /// <summary>按 Enter 打开第一个匹配结果。</summary>
    public RelayCommand OpenFirstSearchResultCommand { get; }

    /// <summary>窗口显示前载入持久化标签目录。</summary>
    public async Task InitializeAsync()
    {
        await Sidebar.RefreshAsync();
        if (_accounts is null) return;
        try { await _accounts.RestoreAsync(); }
        catch (Exception error) { AccountMessage = Text.Error(error, "AccountRestoreFailed"); OnPropertyChanged(nameof(AccountMessage)); }
    }

    /// <summary>发布 JWT 用户名与账号状态的绑定变化。</summary>
    private void OnAccountChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(AccountName)); OnPropertyChanged(nameof(IsSignedIn)); OnPropertyChanged(nameof(AccountStatus));
        AccountMessage = "";
        OnPropertyChanged(nameof(AccountMessage));
    }

    /// <summary>统一刷新主题相关计算属性。</summary>
    private void OnThemeChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(IsDarkTheme));
        OnPropertyChanged(nameof(IsLightTheme));
    }

    /// <summary>增加实例后重算搜索结果。</summary>
    private void OnInstancesChanged(object? sender, EventArgs args) => RefreshGlobalSearch();

    /// <summary>将页签选中态同步到展开和收起导航。</summary>
    private void OnSelectedTabChanged(object? sender, EventArgs args)
    {
        Sidebar.SelectInstance(Workspace.SelectedTab.InstanceId);
        if (Workspace.SelectedTab is { InstanceId: not null } page)
        {
            // 外壳只协调通用生命周期，功能模块自行处理远程资源。
            if (_instancePages.Add(page))
                page.PropertyChanged += OnInstancePageChanged;
            _ = page.InitializeAsync();
        }
        if (Workspace.SelectedTab.Id == "tag:production")
            Sidebar.SelectedTag = "Production";
    }

    /// <summary>编辑连接改变标题时刷新实例快照，页签关闭及时移除更新订阅。</summary>
    private async void OnInstancePageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not WorkspacePageViewModel page || _disposed) return;
        if (args.PropertyName == nameof(WorkspacePageViewModel.IsClosed) && page.IsClosed)
        {
            _instancePages.Remove(page);
            page.PropertyChanged -= OnInstancePageChanged;
        }
        else if (args.PropertyName == nameof(WorkspacePageViewModel.Header))
        {
            try { await Sidebar.RefreshAsync(); }
            catch (Exception) { /* 下次打开设置或刷新导航时再次读取本地快照。 */ }
        }
    }

    /// <summary>使用全部实例搜索，不改变侧栏过滤视图。</summary>
    private void RefreshGlobalSearch()
    {
        GlobalSearchResults.Clear();
        if (!string.IsNullOrWhiteSpace(GlobalSearchQuery))
            foreach (var instance in Sidebar.AllInstances.Where(instance =>
                InstanceSidebarViewModel.MatchesSearch(instance, GlobalSearchQuery)).Take(8))
                GlobalSearchResults.Add(instance);
        OpenFirstSearchResultCommand.NotifyCanExecuteChanged();
    }

    /// <summary>解除服务订阅并等待所有终端释放。</summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        _themes.ThemeChanged -= OnThemeChanged;
        if (_accounts is not null) _accounts.AccountChanged -= OnAccountChanged;
        Sidebar.InstancesChanged -= OnInstancesChanged;
        Workspace.SelectedTabChanged -= OnSelectedTabChanged;
        foreach (var page in _instancePages)
            page.PropertyChanged -= OnInstancePageChanged;
        _instancePages.Clear();
        Sidebar.Dispose();
        await Workspace.DisposeAsync();
    }
}
