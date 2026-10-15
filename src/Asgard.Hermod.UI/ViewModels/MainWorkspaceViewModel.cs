namespace Asgard.Hermod.UI.ViewModels;

/// <summary>主窗体固定导航与表格投影，复用既有配置、标签和独立窗口工作区。</summary>
public sealed class MainWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly InstanceSidebarViewModel _catalog;
    private readonly WorkspaceViewModel _workspace;
    private readonly IInstanceEditor _editor;
    private int _page = 1;
    private int _pageSize = 9;
    private string _query = "";
    private string _error = "";
    private double _viewWidth = 1360;

    /// <summary>所有按钮路由到既有真实应用服务，不生成示例数据。</summary>
    public MainWorkspaceViewModel(InstanceSidebarViewModel catalog, WorkspaceViewModel workspace, IInstanceEditor editor,
        IInstanceProviderRegistry? registry, ISettingsEditor? settings)
    {
        _catalog = catalog; _workspace = workspace; _editor = editor;
        Navigation = [new("overview", Text.Get("MainOverview"), "house"), new("all", Text.Get("MainAllInstances"), "list"),
            new("mysql", Text.Get("MainMySql"), "database", Text.Get("MainDatabaseConnections")),
            new("redis", Text.Get("MainRedis"), "layers", Text.Get("MainCacheConnections")),
            new("ssh", Text.Get("MainSsh"), "terminal", Text.Get("MainServerConnections")),
            new("storage", Text.Get("MainStorage"), "box", Text.Get("MainStorageConnections"))];
        SelectedNavigation = Navigation[0];
        SelectNavigationCommand = new RelayCommand(value => { if (value is MainNavigationItem item) SelectedNavigation = item; });
        OpenCommand = new RelayCommand(value => { if (value is MainInstanceRow row) _catalog.OpenInstanceCommand.Execute(row.Instance); });
        EditCommand = new AsyncRelayCommand(EditAsync, value => value is MainInstanceRow);
        RenameCommand = new AsyncRelayCommand(async value => { if (value is MainInstanceRow row) { await _catalog.RenameInstanceCommand.ExecuteAsync(row.Instance); Refresh(); } });
        ManageTagsCommand = new AsyncRelayCommand(_ => settings?.OpenTagsAsync() ?? Task.CompletedTask);
        PreviousPageCommand = new RelayCommand(_ => { _page--; RefreshRows(); }, _ => _page > 1);
        NextPageCommand = new RelayCommand(_ => { _page++; RefreshRows(); }, _ => _page < PageCount);
        SortOptions = [new("recent", Text.Get("MainSortRecent")), new("name", Text.Get("MainSortName"))];
        SelectedSort = SortOptions[0];
        Providers.Add(new("", Text.Get("MainAllProviders")));
        SelectedProvider = Providers[0];
        catalog.InstancesChanged += OnCatalogChanged;
        catalog.PropertyChanged += OnCatalogPropertyChanged;
        workspace.Home.PropertyChanged += OnHistoryChanged;
        workspace.PropertyChanged += OnWorkspaceChanged;
        Refresh();
    }

    /// <summary>六个固定页签顺序与设计稿一致。</summary>
    public IReadOnlyList<MainNavigationItem> Navigation { get; }
    /// <summary>概览四种类型入口。</summary>
    public IEnumerable<MainNavigationItem> Types => Navigation.Skip(2);
    /// <summary>切换只改变主窗体视图，不建立连接。</summary>
    public MainNavigationItem SelectedNavigation
    {
        get;
        set
        {
            if (value is null || !Navigation.Contains(value) || !SetProperty(ref field, value)) return;
            _page = 1; SelectedRow = null;
            foreach (string name in new[] { nameof(IsOverview), nameof(Title), nameof(Description), nameof(OverviewVisibility), nameof(ListVisibility), nameof(SectionMargin), nameof(FooterHeight) }) OnPropertyChanged(name);
            SetListWidth(_viewWidth);
            RefreshRows();
        }
    } = null!;
    /// <summary>概览不与旧首页卡片混用。</summary>
    public bool IsOverview => SelectedNavigation.Id == "overview";
    /// <summary>固定页签中的类型作为过滤条件。</summary>
    public Visibility OverviewVisibility => IsOverview ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>全部及类型列表共用真实数据投影。</summary>
    public Visibility ListVisibility => IsOverview ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>正文标题。</summary>
    public string Title => IsOverview ? Text.Get("MainWorkbench") : SelectedNavigation.Title;
    /// <summary>完整本地化句子，不用数量片段拼接说明。</summary>
    public string Description => IsOverview ? Text.Get("MainOpenHint") : Text.Format("MainListDescription", Filtered().Count());
    /// <summary>列表搜索名称、安全地址和标签，不读取连接参数。</summary>
    public string Query { get => _query; set { if (SetProperty(ref _query, value)) { _page = 1; RefreshRows(); } } }
    /// <summary>选中行不自动打开连接。</summary>
    public MainInstanceRow? SelectedRow { get; set => SetProperty(ref field, value); }
    /// <summary>当前页或最近访问表格。</summary>
    public ObservableCollection<MainInstanceRow> Rows { get; } = [];
    /// <summary>提供商选项按注册身份过滤，未知连接器仍可见。</summary>
    public ObservableCollection<MainFilterOption> Providers { get; } = [];
    /// <summary>当前提供商过滤。</summary>
    public MainFilterOption SelectedProvider { get; set { if (SetProperty(ref field, value)) { _page = 1; RefreshRows(); } } } = null!;
    /// <summary>最近打开与名称排序均为稳定标识。</summary>
    public IReadOnlyList<MainFilterOption> SortOptions { get; }
    /// <summary>切换排序保留筛选并回到第一页。</summary>
    public MainFilterOption SelectedSort { get; set { if (SetProperty(ref field, value)) { _page = 1; RefreshRows(); } } } = null!;
    /// <summary>复用既有多选标签草稿。</summary>
    public InstanceSidebarViewModel Catalog => _catalog;
    /// <summary>标签筛选的已应用状态。</summary>
    public string TagFilterLabel => _catalog.SelectedTagPaths.Count == 0 ? Text.Get("MainAllTags") : _catalog.TagFilterLabel;
    /// <summary>真实配置总数，不包含内置本地终端。</summary>
    public string TotalText => Text.Format("MainCount", _catalog.AllInstances.Count(item => !item.IsLocal));
    /// <summary>最近访问表头数量。</summary>
    public string RecentText => Text.Format("MainCount", Rows.Count);
    /// <summary>当前过滤结果范围。</summary>
    public string RangeText => Text.Format("MainRange", Rows.Count == 0 ? 0 : (_page - 1) * _pageSize + 1,
        Math.Min(_page * _pageSize, Filtered().Count()), Filtered().Count());
    /// <summary>分页由实际结果数量决定。</summary>
    public int PageCount => Math.Max(1, (int)Math.Ceiling(Filtered().Count() / (double)_pageSize));
    /// <summary>当前位置。</summary>
    public string PageText => Text.Format("MainPage", _page, PageCount);
    /// <summary>搜索或空配置状态。</summary>
    public Visibility EmptyVisibility => Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    /// <summary>概览没有筛选表单，空状态不能要求调整不存在的筛选条件。</summary>
    public string EmptyText => Text.Get(IsOverview ? "MainRecentEmpty" : "MainEmptyList");
    /// <summary>错误仅用共享安全消息呈现。</summary>
    public string Error => _workspace.OpenError.Length > 0 ? _workspace.OpenError : _error.Length > 0 ? _error : _catalog.OperationError;
    /// <summary>无错误时不占正文高度。</summary>
    public Visibility ErrorVisibility => Error.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    /// <summary>概览与列表分别采用稿中的二十和十八像素间距。</summary>
    public Thickness SectionMargin => new(0, IsOverview ? 20 : 18, 0, IsOverview ? 20 : 18);
    /// <summary>最近访问底部留白与列表分页分别匹配原稿。</summary>
    public double FooterHeight => IsOverview ? 65 : 45;
    /// <summary>概览和列表使用不同的行高。</summary>
    public double RowHeight => IsOverview ? 64 : 48;
    /// <summary>概览和列表对应设计稿的表头高度。</summary>
    public double HeaderHeight => IsOverview ? 32 : 40;
    /// <summary>宽屏严格对应稿中名称列，窄窗保留所有操作和筛选。</summary>
    public GridLength NameColumnWidth { get; private set; } = new(430);
    /// <summary>提供商列不与安全地址共用数据。</summary>
    public GridLength ProviderColumnWidth { get; private set; } = new(160);
    /// <summary>列宽只响应视图可用空间，不影响配置和工作窗口。</summary>
    public void SetListWidth(double width)
    {
        _viewWidth = width;
        NameColumnWidth = new(width < 1150 ? 260 : IsOverview ? 480 : 430);
        ProviderColumnWidth = new(width < 1150 ? 120 : 160);
        OnPropertyChanged(nameof(NameColumnWidth)); OnPropertyChanged(nameof(ProviderColumnWidth));
    }
    /// <summary>主动作复用实际创建向导。</summary>
    public ICommand AddInstanceCommand => _catalog.AddInstanceCommand;
    /// <summary>内置本地终端不计入配置数量。</summary>
    public ICommand OpenLocalTerminalCommand => _catalog.OpenLocalTerminalCommand;
    /// <summary>标签管理直达既有设置页。</summary>
    public AsyncRelayCommand ManageTagsCommand { get; }
    /// <summary>固定导航命令。</summary>
    public RelayCommand SelectNavigationCommand { get; }
    /// <summary>只有双击、Enter 或菜单打开才建立工作窗口。</summary>
    public RelayCommand OpenCommand { get; }
    /// <summary>原子修改名称与标签。</summary>
    public AsyncRelayCommand RenameCommand { get; }
    /// <summary>类型专属编辑向导。</summary>
    public AsyncRelayCommand EditCommand { get; }
    /// <summary>上一页。</summary>
    public RelayCommand PreviousPageCommand { get; }
    /// <summary>下一页。</summary>
    public RelayCommand NextPageCommand { get; }

    /// <summary>窗口缩放后按可容纳行数分页，保留首个结果所在页。</summary>
    public void SetListHeight(double height)
    {
        int size = Math.Clamp((int)Math.Floor((height - 85) / 48), 1, 9);
        if (size == _pageSize) return;
        int first = (_page - 1) * _pageSize; _pageSize = size; _page = first / size + 1; RefreshRows();
    }
    private IEnumerable<MainInstanceRow> AllRows()
    {
        var history = _workspace.Home.VisitHistory.ToDictionary(item => item.InstanceId, item => item.OpenedAt);
        return _catalog.AllInstances.Where(item => !item.IsLocal).Select(item =>
            new MainInstanceRow(item, history.TryGetValue(item.Id, out var opened) ? opened : null));
    }
    private IEnumerable<MainInstanceRow> Filtered()
    {
        var rows = AllRows();
        if (SelectedNavigation is not null && SelectedNavigation.Id is not ("all" or "overview")) rows = rows.Where(row => row.Instance.Summary.TypeId == SelectedNavigation.Id);
        if (SelectedProvider is { Id.Length: > 0 }) rows = rows.Where(row => row.ProviderFilterId == SelectedProvider.Id);
        var matchingTags = _catalog.Instances.Cast<InstanceItemViewModel>().Select(item => item.Id).ToHashSet();
        rows = rows.Where(row => matchingTags.Contains(row.Instance.Id));
        string query = Query.Trim();
        if (query.Length > 0) rows = rows.Where(row => InstanceSidebarViewModel.MatchesSearch(row.Instance, query) || row.Address.Contains(query, StringComparison.OrdinalIgnoreCase));
        return SelectedSort?.Id == "name" ? rows.OrderBy(row => row.Instance.Name, StringComparer.CurrentCultureIgnoreCase)
            : rows.OrderByDescending(row => row.OpenedAt).ThenBy(row => row.Instance.Name, StringComparer.CurrentCultureIgnoreCase);
    }
    private void Refresh()
    {
        foreach (var type in Types) type.Count = _catalog.AllInstances.Count(item => !item.IsLocal && item.Summary.TypeId == type.Id);
        string provider = SelectedProvider?.Id ?? "";
        Providers.Clear(); Providers.Add(new("", Text.Get("MainAllProviders")));
        foreach (var group in AllRows().GroupBy(row => row.ProviderFilterId).OrderBy(group => group.First().ProviderName))
            Providers.Add(new(group.Key, group.First().ProviderName));
        SelectedProvider = Providers.FirstOrDefault(option => option.Id == provider) ?? Providers[0];
        RefreshRows(); OnPropertyChanged(nameof(TotalText));
    }
    private void RefreshRows()
    {
        if (SelectedNavigation is null || _workspace is null) return;
        string? selected = SelectedRow?.Instance.Id;
        _page = Math.Clamp(_page, 1, PageCount);
        var rows = IsOverview ? AllRows().Where(row => row.OpenedAt is not null).OrderByDescending(row => row.OpenedAt).Take(6)
            : Filtered().Skip((_page - 1) * _pageSize).Take(_pageSize);
        Rows.Clear(); foreach (var row in rows) Rows.Add(row);
        SelectedRow = Rows.FirstOrDefault(row => row.Instance.Id == selected);
        foreach (string name in new[] { nameof(Description), nameof(RecentText), nameof(RangeText), nameof(PageText), nameof(EmptyVisibility), nameof(EmptyText), nameof(TagFilterLabel), nameof(RowHeight), nameof(HeaderHeight), nameof(Error), nameof(ErrorVisibility) }) OnPropertyChanged(name);
        PreviousPageCommand?.NotifyCanExecuteChanged(); NextPageCommand?.NotifyCanExecuteChanged();
    }
    private async Task EditAsync(object? value)
    {
        if (value is not MainInstanceRow row) return;
        try { await _editor.EditAsync(row.Instance.Id); await _catalog.RefreshAsync(); _error = ""; }
        catch (Exception error) { _error = Text.Error(error); }
        RefreshRows();
    }
    private void OnCatalogChanged(object? sender, EventArgs args) => Refresh();
    private void OnCatalogPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstanceSidebarViewModel.SelectedTagPaths) or nameof(InstanceSidebarViewModel.IsEmpty) or nameof(InstanceSidebarViewModel.OperationError)) { _page = 1; RefreshRows(); }
    }
    private void OnHistoryChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(HomeWorkspaceViewModel.HasRecentInstances)) RefreshRows(); }
    private void OnWorkspaceChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(WorkspaceViewModel.OpenError)) { OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(ErrorVisibility)); } }
    /// <inheritdoc />
    public void Dispose()
    {
        _catalog.InstancesChanged -= OnCatalogChanged; _catalog.PropertyChanged -= OnCatalogPropertyChanged;
        _workspace.Home.PropertyChanged -= OnHistoryChanged;
        _workspace.PropertyChanged -= OnWorkspaceChanged;
    }
}
