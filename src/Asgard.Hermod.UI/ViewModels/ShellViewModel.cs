using Asgard.Hermod.UI.Themes;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>协调外壳、导航与工作区，主题和连接通过宿主服务接入。</summary>
public sealed class ShellViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IThemeService _themes;
    private bool _isSidebarExpanded = true;
    private string _globalSearchQuery = "";
    private bool _disposed;

    /// <summary>装配外壳，构造时不启动 Shell 或远程连接。</summary>
    public ShellViewModel(IWorkspaceDataSource source, IInstanceEditor editor,
        ITerminalSessionFactory terminalFactory, IThemeService themes,
        ISettingsEditor? settings = null, ITagManagementService? tags = null)
    {
        ArgumentNullException.ThrowIfNull(themes);
        _themes = themes;
        Workspace = new WorkspaceViewModel(source, terminalFactory);
        Sidebar = new InstanceSidebarViewModel(source, editor, Workspace.OpenInstance, Workspace.OpenLocalTerminal, tags);
        OpenSettingsCommand = new AsyncRelayCommand(_ => settings?.OpenAsync() ?? Task.CompletedTask,
            _ => settings is not null);
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
    public string WorkspaceName => "本地工作区";
    /// <summary>未接入身份服务时显示本地模式。</summary>
    public string AccountName => "本地";
    /// <summary>当前未接入账户登录。</summary>
    public bool IsSignedIn => false;
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
    /// <summary>打开指定搜索结果。</summary>
    public RelayCommand OpenGlobalSearchResultCommand { get; }
    /// <summary>按 Enter 打开第一个匹配结果。</summary>
    public RelayCommand OpenFirstSearchResultCommand { get; }

    /// <summary>窗口显示前载入持久化标签目录。</summary>
    public Task InitializeAsync() => Sidebar.RefreshAsync();

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
        if (Workspace.SelectedTab.Id == "tag:production")
            Sidebar.SelectedTag = "Production";
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
        Sidebar.InstancesChanged -= OnInstancesChanged;
        Workspace.SelectedTabChanged -= OnSelectedTabChanged;
        Sidebar.Dispose();
        await Workspace.DisposeAsync();
    }
}
