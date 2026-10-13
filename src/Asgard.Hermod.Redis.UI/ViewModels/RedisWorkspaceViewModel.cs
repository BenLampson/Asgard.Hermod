using Asgard.Hermod.Redis.UI.Services;

namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>实例页面拥有一个连接和多个内部键页签，外层实例页签仍由 AppShell 管理。</summary>
public sealed class RedisWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly IInstanceConfigurationService _configurations;
    private readonly IRedisSessionFactory _connections;
    private readonly IInstanceEditor _editor;
    private readonly IRedisWorkspaceDialogs _dialogs;
    private readonly InstanceItemViewModel _instance;
    private readonly CancellationTokenSource _lifetime = new();
    private IRedisSession? _session;
    private readonly Dictionary<string, RedisKeyTree> _trees = new(StringComparer.Ordinal);
    private bool _initialized;
    private int _database;
    private bool _readingDatabaseCounts;

    /// <summary>不打开真实连接，首次激活时由公共页签生命周期初始化。</summary>
    public RedisWorkspaceViewModel(InstanceItemViewModel instance, IInstanceConfigurationService configurations, IRedisSessionFactory connections,
        IInstanceEditor editor, IRedisWorkspaceDialogs dialogs) : base("redis:" + Guid.NewGuid().ToString("N"), instance.Name, "database", instance.Id)
    {
        _instance = instance; _configurations = configurations; _connections = connections; _editor = editor; _dialogs = dialogs;
        OpenKnownCommand = new AsyncRelayCommand(_ => OpenKnownAsync(), _ => _session is not null && KnownKey.Length > 0);
        QueryCommand = new AsyncRelayCommand(_ => QueryAsync(), _ => _session is not null);
        RefreshTreeCommand = new AsyncRelayCommand(_ => RefreshTreeAsync(), _ => _session is not null);
        NewKeyCommand = new AsyncRelayCommand(_ => CreateAsync(), _ => _session is not null);
        CloseTabCommand = new AsyncRelayCommand(tab => CloseTabAsync(tab as RedisKeyTabViewModel));
        ActivateTabCommand = new RelayCommand(tab => { if (tab is RedisKeyTabViewModel key) ActiveTab = key; });
        ReconnectCommand = new AsyncRelayCommand(_ => ConnectAsync(), _ => !IsConnecting && Tabs.All(tab => !tab.IsDirty && !tab.IsBusy));
        EditConnectionCommand = new AsyncRelayCommand(_ => EditConnectionAsync(), _ => !IsConnecting);
    }
    /// <summary>实例标题。</summary>
    public string Name => Header;
    /// <summary>只显示公开端点。</summary>
    public string Endpoint { get; private set; } = "";
    /// <summary>公共状态徽标。</summary>
    public string Status => Text.Get(IsConnecting ? "RedisConnecting" : _session is null ? "RedisConnectionFailed" : "RedisConnected");
    /// <summary>连接中可以等待或关闭，禁止重复连接。</summary>
    public bool IsConnecting { get; private set; }
    /// <summary>实际会话建立状态。</summary>
    public bool IsConnected => _session is not null;
    /// <summary>安全错误，不展示第三方异常。</summary>
    public string Error { get; private set; } = "";
    /// <summary>共享通知插槽只在有反馈时显示。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>数据库文本使用 invariant，不随界面语言改变。</summary>
    public string DatabaseText { get; set => SetProperty(ref field, value); } = "0";
    /// <summary>选项来自服务器实际数据库数量，不假设固定十六个数据库。</summary>
    public IReadOnlyList<RedisDatabaseViewModel> Databases { get; private set; } = [];
    /// <summary>DBSIZE 不遍历键或加载值；独立更新标签，不改变数据库选择。</summary>
    public async Task RefreshDatabaseCountsAsync()
    {
        if (_session is not { } session || _readingDatabaseCounts) return;
        _readingDatabaseCounts = true;
        try
        {
            var choices = Databases;
            foreach (var choice in choices)
            {
                long? count = await session.ReadDatabaseKeyCountAsync(choice.Database, _lifetime.Token);
                if (!ReferenceEquals(session, _session) || IsClosed) return;
                choice.SetCount(count);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        finally { _readingDatabaseCounts = false; }
    }
    /// <summary>状态栏只展示协议提供的拓扑和公开认证身份。</summary>
    public string ConnectionSummary => _session is null ? Status : Text.Format("RedisConnectionSummary",
        Text.Get(_session.Options.Topology switch { RedisIds.Cluster => "RedisCluster", RedisIds.Sentinel => "RedisSentinel", _ => "RedisStandalone" }),
        _session.Options.Value("tls", "no") == "yes" ? "TLS" : "TCP",
        _session.Options.Value("auth", "none") == "acl" ? "ACL " + _session.Options.Value("user") : Text.Get(_session.Options.Value("auth", "none") == "password" ? "SshPasswordAuthentication" : "RedisNoAuthentication"));
    /// <summary>查询仅控制键树，不改变现有页签草稿。</summary>
    public string Pattern { get; set => SetProperty(ref field, value); } = "";
    /// <summary>类型下拉使用协议标识。</summary>
    public IReadOnlyList<InstanceFieldChoice> Types { get; } = [new("", Text.Get("RedisAllTypes")), new("string", "string"), new("hash", "hash"),
        new("list", "list"), new("set", "set"), new("zset", "zset"), new("stream", "stream")];
    /// <summary>类型筛选标识。</summary>
    public string SelectedType { get; set => SetProperty(ref field, value); } = "";
    /// <summary>已知键即使没有 SCAN 权限，也可直接读取。</summary>
    public string KnownKey { get; set { if (SetProperty(ref field, value)) OpenKnownCommand.NotifyCanExecuteChanged(); } } = "";
    /// <summary>选中的树分组仅作为新增键名的字面前缀。</summary>
    public string CreatePrefix { get; set => SetProperty(ref field, value); } = "";
    /// <summary>本查询范围的懒加载树。</summary>
    public RedisKeyTree? Tree { get; private set; }
    /// <summary>页签顺序保持打开顺序，重复点击只激活。</summary>
    public ObservableCollection<RedisKeyTabViewModel> Tabs { get; } = [];
    /// <summary>活动键页签，关闭最后一个后显示空态。</summary>
    public RedisKeyTabViewModel? ActiveTab
    {
        get;
        set
        {
            if (ReferenceEquals(field, value)) return;
            if (field is not null) field.IsActive = false;
            field = value; if (value is not null) value.IsActive = true;
            OnPropertyChanged(); OnPropertyChanged(nameof(HasActiveTab)); OnPropertyChanged(nameof(IsEmpty));
            foreach (var tree in _trees.Values) tree.SetActiveKey(value?.Database == tree.Database ? value.Key : null);
        }
    }
    /// <summary>只显示当前页签的内容。</summary>
    public bool HasActiveTab => ActiveTab is not null;
    /// <summary>键树保持显示，不因为关闭最后一个键而消失。</summary>
    public bool IsEmpty => ActiveTab is null;
    /// <summary>窗口缩小时控制单列的树或内容切换。</summary>
    public bool ShowTree { get; set => SetProperty(ref field, value); } = true;
    public AsyncRelayCommand OpenKnownCommand { get; }
    public AsyncRelayCommand QueryCommand { get; }
    public AsyncRelayCommand RefreshTreeCommand { get; }
    public AsyncRelayCommand NewKeyCommand { get; }
    public AsyncRelayCommand CloseTabCommand { get; }
    public RelayCommand ActivateTabCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand EditConnectionCommand { get; }

    /// <inheritdoc />
    public override async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return; _initialized = true;
        await ConnectAsync(cancellationToken);
    }
    private async Task ConnectAsync(CancellationToken token = default)
    {
        if (IsConnecting || IsClosed || Tabs.Any(tab => tab.IsDirty || tab.IsBusy)) return;
        IsConnecting = true; Error = ""; Refresh();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, token);
        try
        {
            foreach (var tree in _trees.Values) tree.Dispose(); _trees.Clear(); Tree = null;
            ReleaseTabs();
            if (_session is not null) await _session.DisposeAsync(); _session = null;
            var options = await _configurations.GetConnectionAsync(InstanceId!, linked.Token);
            if (options is null) throw Text.Exception("RedisConfigurationInvalid");
            var session = await _connections.OpenAsync(options, linked.Token);
            if (linked.IsCancellationRequested || IsClosed) { await session.DisposeAsync(); return; }
            _session = session; _database = session.Options.Database; DatabaseText = _database.ToString(CultureInfo.InvariantCulture);
            Databases = Enumerable.Range(0, session.DatabaseCount).Select(database => new RedisDatabaseViewModel(database)).ToArray();
            OnPropertyChanged(nameof(Databases));
            await RefreshDatabaseCountsAsync();
            Endpoint = string.Join(", ", session.Options.Endpoints); await QueryAsync();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { Error = Text.Error(error, "RedisConnectionFailed"); }
        finally { IsConnecting = false; Refresh(); }
    }

    /// <summary>切换数据库和查询复用对应树缓存，草稿页签独立保留。</summary>
    public async Task QueryAsync()
    {
        if (_session is null) return;
        try
        {
            if (!int.TryParse(DatabaseText, NumberStyles.None, CultureInfo.InvariantCulture, out int database) || database < 0)
                throw Text.Exception("RedisDatabaseInvalid");
            if (_session.Options.Topology == RedisIds.Cluster && database != 0) throw Text.Exception("RedisClusterDatabaseZero");
            Tree?.Suspend(); _database = database;
            string scope = database.ToString(CultureInfo.InvariantCulture) + ":" + Pattern.Length.ToString(CultureInfo.InvariantCulture) + ":" + Pattern + SelectedType;
            if (!_trees.TryGetValue(scope, out var tree))
            {
                if (_trees.Count >= 16) { var oldest = _trees.First(); oldest.Value.Dispose(); _trees.Remove(oldest.Key); }
                tree = new(_session, database, Pattern, SelectedType); _trees.Add(scope, tree);
            }
            Tree = tree;
            foreach (var tab in Tabs.Where(tab => tab.Database == database)) Tree.Remember(tab.Key, tab.Type, tab.Ttl);
            Tree.SetActiveKey(ActiveTab?.Database == database ? ActiveTab.Key : null);
            Error = ""; OnPropertyChanged(nameof(Tree)); await Tree.LoadAsync(Tree.Root);
        }
        catch (Exception error) { Error = Text.Error(error, "RedisCommandFailed"); }
        Refresh();
    }

    /// <summary>由树的单次键选择调用；不支持键复选框或批量操作。</summary>
    public async Task OpenAsync(RedisKeyId key)
    {
        if (_session is null) return;
        var tab = Tabs.FirstOrDefault(item => item.Database == _database && item.Key.Equals(key));
        if (tab is not null) { ActiveTab = tab; ShowTree = false; return; }
        tab = new(_session, _database, key, _dialogs); tab.Deleted += RemoveTab; tab.PropertyChanged += OnKeyChanged;
        Tabs.Add(tab); ActiveTab = tab; ShowTree = false; await tab.ReadAsync();
    }
    private Task OpenKnownAsync() => OpenAsync(RedisKeyId.FromText(KnownKey));
    private async Task RefreshTreeAsync()
    {
        foreach (var tree in _trees.Values) tree.Dispose(); _trees.Clear(); Tree = null;
        await QueryAsync();
    }
    private async Task CloseTabAsync(RedisKeyTabViewModel? tab)
    {
        if (tab is not null && await tab.RequestCloseAsync()) RemoveTab(tab);
    }
    private void RemoveTab(RedisKeyTabViewModel tab)
    {
        int index = Tabs.IndexOf(tab); if (index < 0) return;
        bool active = ReferenceEquals(ActiveTab, tab); Tabs.Remove(tab); tab.Deleted -= RemoveTab; tab.PropertyChanged -= OnKeyChanged; tab.Dispose();
        if (active) ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        if (ActiveTab is null) ShowTree = true; ReconnectCommand.NotifyCanExecuteChanged();
    }
    /// <summary>Ctrl+Tab 按打开顺序切换内部键页签。</summary>
    public void CycleTab(int direction)
    {
        if (Tabs.Count == 0) return;
        int index = ActiveTab is null ? 0 : Tabs.IndexOf(ActiveTab);
        ActiveTab = Tabs[(index + direction + Tabs.Count) % Tabs.Count];
    }
    private async Task CreateAsync()
    {
        if (_session is null) return;
        int database = _database;
        try
        {
            var key = await _dialogs.CreateAsync(_session, database, CreatePrefix, _lifetime.Token);
            if (key is null || IsClosed) return;
            DatabaseText = database.ToString(CultureInfo.InvariantCulture); _database = database;
            await OpenAsync(key); Pattern = ""; SelectedType = ""; await QueryAsync(); Tree?.Reveal(key); await RefreshDatabaseCountsAsync();
        }
        catch (Exception error) { Error = Text.Error(error, "RedisCommandFailed"); Refresh(); }
    }
    private void OnKeyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not RedisKeyTabViewModel tab) return;
        if (args.PropertyName is nameof(RedisKeyTabViewModel.Type) or nameof(RedisKeyTabViewModel.Ttl))
            foreach (var tree in _trees.Values.Where(tree => tree.Database == tab.Database)) tree.Remember(tab.Key, tab.Type, tab.Ttl);
        if (args.PropertyName is nameof(RedisKeyTabViewModel.IsDirty) or nameof(RedisKeyTabViewModel.IsBusy)) ReconnectCommand.NotifyCanExecuteChanged();
    }
    private async Task EditConnectionAsync()
    {
        try { if (await _editor.EditAsync(InstanceId!) is { } updated) { _instance.UpdateSnapshot(updated); Header = updated.Name; OnPropertyChanged(nameof(Name)); } }
        catch (Exception error) { Error = Text.Error(error, "RedisConfigurationInvalid"); Refresh(); }
    }
    /// <inheritdoc />
    public override async Task<bool> RequestCloseAsync()
    {
        foreach (var tab in Tabs.ToArray()) if (!await tab.RequestCloseAsync()) return false;
        return true;
    }
    /// <inheritdoc />
    protected override async ValueTask ReleaseAsync()
    {
        _lifetime.Cancel(); foreach (var tree in _trees.Values) tree.Dispose(); _trees.Clear();
        ReleaseTabs();
        if (_session is not null) await _session.DisposeAsync(); _session = null; _lifetime.Dispose();
    }
    private void ReleaseTabs()
    {
        foreach (var tab in Tabs) { tab.Deleted -= RemoveTab; tab.PropertyChanged -= OnKeyChanged; tab.Dispose(); }
        Tabs.Clear(); ActiveTab = null;
    }
    private void Refresh()
    {
        foreach (string name in new[] { nameof(Status), nameof(ConnectionSummary), nameof(IsConnecting), nameof(IsConnected), nameof(Error), nameof(HasError), nameof(Endpoint) }) OnPropertyChanged(name);
        OpenKnownCommand.NotifyCanExecuteChanged(); QueryCommand.NotifyCanExecuteChanged(); NewKeyCommand.NotifyCanExecuteChanged();
        ReconnectCommand.NotifyCanExecuteChanged(); EditConnectionCommand.NotifyCanExecuteChanged();
        RefreshTreeCommand.NotifyCanExecuteChanged();
    }
}
