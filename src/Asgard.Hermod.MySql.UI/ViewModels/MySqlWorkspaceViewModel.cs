using Asgard.Hermod.Agent;
using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>公共外壳管理实例页签，此页面管理对象树和独立的内部会话页签。</summary>
public sealed partial class MySqlWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly InstanceItemViewModel _instance;
    private readonly IInstanceConfigurationService _configurations;
    private readonly ConnectionAwareMySqlSessionFactory _factory;
    private readonly IMySqlSessionFactory _sessionFactory;
    private readonly IAgentClient? _agentClient;
    private readonly IInstanceEditor _editor;
    private readonly IMySqlWorkspaceDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private IMySqlSession? _metadata;
    private InstanceConnectionOptions? _options;
    private Task? _initializeTask;
    private int _queryNumber;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlWorkspaceViewModel(InstanceItemViewModel instance, IInstanceConfigurationService configurations, IMySqlSessionFactory factory,
        IInstanceEditor editor, IMySqlWorkspaceDialogs dialogs, string? focusedDatabase = null, IAgentClient? agentClient = null) : base("mysql:" + Guid.NewGuid().ToString("N"), instance.Name, "database", instance.Id)
    {
        _instance = instance; _configurations = configurations; _factory = new ConnectionAwareMySqlSessionFactory(factory, OnConnectionLost); _editor = editor; _dialogs = dialogs;
        _sessionFactory = factory; _agentClient = agentClient; FocusedDatabase = focusedDatabase ?? ""; UpdateInstanceMetadata(instance.Summary);
        NewQueryCommand = new RelayCommand(_ => NewQuery(), _ => IsConnected && !IsConnecting);
        NewCrossDatabaseQueryCommand = new RelayCommand(_ => NewQuery(""), _ => IsConnected && !IsConnecting);
        BrowseDatabasesCommand = new RelayCommand(_ => RequestWorkspace(null, () => CreateWorkspace(null)));
        CloseTabCommand = new AsyncRelayCommand(tab => CloseTabAsync(tab as MySqlTabViewModel));
        RefreshTreeCommand = new AsyncRelayCommand(_ => RefreshTreeAsync(), _ => IsConnected && Tree is not null && !IsConnecting);
        ReconnectCommand = new AsyncRelayCommand(_ => ReconnectAsync(), _ => !IsConnecting);
        EditConnectionCommand = new AsyncRelayCommand(_ => EditConnectionAsync(), _ => !IsConnecting);
        OpenQueryCommand = new AsyncRelayCommand(_ => OpenQueryAsync(), _ => IsConnected && !IsConnecting);
        ExportCommand = new AsyncRelayCommand(_ => ActiveTab?.ExportAsync() ?? Task.CompletedTask, _ => ActiveTab?.HasResult == true && !ActiveTab.IsBusy);
        Tools = [new("query.new", Text.Get("MySqlNewQuery"), "file-code", NewQueryCommand), new("query.open", Text.Get("MySqlOpenQuery"), "file", OpenQueryCommand),
            new("result.export", Text.Get("MySqlExportLoaded"), "download", ExportCommand), new("tree.refresh", Text.Get("MySqlRefreshBranch"), "refresh-cw", RefreshTreeCommand),
            new("connection.reconnect", Text.Get("MySqlReconnect"), "refresh-cw", ReconnectCommand),
            new("query.crossDatabase", Text.Get("MySqlNewCrossDatabaseQuery"), "file-code", NewCrossDatabaseQueryCommand),
            new("database.browse", Text.Get("MySqlBrowseDatabases"), "database", BrowseDatabasesCommand)];
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
        InitializeAssistant(agentClient);
    }
    /// <summary>对象原始名称或已本地化的文稿标题。</summary>
    public string Name => Header;
    /// <summary>公共环境徽标直接绑定稳定实例元数据。</summary>
    public InstanceItemViewModel Instance => _instance;
    /// <summary>聚焦窗口固定的数据库；空串表示实例浏览窗口。</summary>
    public string FocusedDatabase { get; }
    /// <summary>聚焦状态不跟随查询页签的独立执行上下文变化。</summary>
    public bool IsDatabaseFocused => FocusedDatabase.Length > 0;
    /// <inheritdoc />
    public override string? WorkspaceContextId => IsDatabaseFocused ? "mysql.database:" + FocusedDatabase : null;
    /// <summary>窗口标题保留实例与数据库，重命名实例时不丢失数据库身份。</summary>
    public override void UpdateInstanceMetadata(InstanceSummary instance)
    {
        if (InstanceId != instance.Id) return;
        Header = IsDatabaseFocused ? Text.Format("MySqlDatabaseWorkspaceTitle", FocusedDatabase, instance.Name) : instance.Name;
        OnPropertyChanged(nameof(Name));
    }
    /// <summary>不含凭据的公开端点。</summary>
    public string Endpoint { get; private set; } = "";
    /// <summary>实例连接初始化状态。</summary>
    public bool IsConnecting { get; private set; }
    /// <summary>共享浮层只负责当前连接等待和安全失败反馈。</summary>
    public WorkspaceConnectionViewModel Connection { get; } = new();
    /// <inheritdoc />
    public override WorkspaceConnectionViewModel ConnectionState => Connection;
    /// <summary>徽标按真实连接状态选择语义色。</summary>
    public StatusTone ConnectionTone => IsConnecting ? StatusTone.Accent : IsConnected ? StatusTone.Success : Connection.HasError ? StatusTone.Danger : StatusTone.Muted;
    /// <summary>当前物理连接是否可用；失败后不自动重连。</summary>
    public bool IsConnected => !IsClosed && !_connectionLost && _metadata?.IsConnected == true;
    /// <summary>读取状态或安全失败说明。</summary>
    public string Status => Text.Get(IsConnecting ? "MySqlConnecting" : IsConnected ? "MySqlConnected" : "MySqlDisconnected");
    /// <summary>可安全展示的本地化错误。</summary>
    public string Error { get; private set; } = "";
    /// <summary>当前是否有需要呈现的错误。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>此实例的分支懒加载对象树。</summary>
    public MySqlObjectTree? Tree { get; private set; }
    /// <summary>聚焦窗口直接呈现该库的对象分组，避免重复的实例和数据库层级。</summary>
    public MySqlTreeNode? NavigationRoot { get; private set; }
    /// <summary>对象导航标题包含当前固定数据库名。</summary>
    public string NavigationTitle => IsDatabaseFocused ? FocusedDatabase : Text.Get("MySqlDatabaseObjects");
    /// <summary>窗口次标题优先标明固定数据库，端点仍保留在提示中。</summary>
    public string WorkspaceSubtitle => IsDatabaseFocused ? FocusedDatabase : Endpoint;
    /// <summary>当前操作的树分支，不改变查询数据库上下文。</summary>
    public MySqlTreeNode? SelectedNode { get; set; }
    /// <summary>只对已加载对象进行层级过滤。</summary>
    public string Search { get; set { if (SetProperty(ref field, value)) Tree?.Filter(value); } } = "";
    /// <summary>按打开顺序保存的独立对象和查询页签。</summary>
    public ObservableCollection<MySqlTabViewModel> Tabs { get; } = [];
    /// <summary>最后一个页签关闭后展示引导，并隐藏空页签轨道。</summary>
    public bool HasTabs => Tabs.Count > 0;
    public MySqlTabViewModel? ActiveTab
    {
        get; set
        {
            if (ReferenceEquals(field, value)) return;
            if (field?.CommitInputRequested?.Invoke() == false) { OnPropertyChanged(); return; }
            if (field is not null) field.IsActive = false;
            field = value; if (value is not null) value.IsActive = true;
            OnPropertyChanged(); ExportCommand.NotifyCanExecuteChanged(); OnActiveTabChanged();
        }
    }
    /// <summary>公共工具面板中的稳定入口。</summary>
    public IReadOnlyList<WorkspaceTool> Tools { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public RelayCommand NewQueryCommand { get; }
    /// <summary>创建允许使用全限定表名的独立跨库 SQL 文稿。</summary>
    public RelayCommand NewCrossDatabaseQueryCommand { get; }
    /// <summary>激活实例的数据库浏览入口，不关闭当前查询或草稿。</summary>
    public RelayCommand BrowseDatabasesCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand CloseTabCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand RefreshTreeCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand ReconnectCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand EditConnectionCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand OpenQueryCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand ExportCommand { get; }
    /// <summary>首次激活页面时建立元数据连接。</summary>
    public override Task InitializeAsync(CancellationToken cancellationToken = default) => _initializeTask ??= ConnectAsync(cancellationToken);
    private async Task ConnectAsync(CancellationToken token = default)
    {
        if (IsConnecting || IsClosed) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        IsConnecting = true; Error = ""; Connection.Begin(linked.Cancel); Refresh(); string connectionError = "";
        try
        {
            _factory.Reset(); await StopConnectionWatchAsync(); _connectionLost = false;
            Tree?.Dispose(); Tree = null; NavigationRoot = null; SelectedNode = null; if (_metadata is not null) await _metadata.DisposeAsync(); _metadata = null;
            _options = await _configurations.GetConnectionAsync(InstanceId!, linked.Token) ?? throw Text.Exception("MySqlConfigurationInvalid");
            var session = await _factory.OpenAsync(_options, linked.Token);
            if (IsClosed || linked.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _metadata = session; Endpoint = Text.Format("MySqlEndpoint", session.Options.Host, session.Options.Port);
            Tree = new(session, _lifetime.Token); OnPropertyChanged(nameof(Tree)); await Tree.LoadAsync(Tree.Root).WaitAsync(linked.Token);
            NavigationRoot = Tree.Root;
            if (IsDatabaseFocused)
            {
                NavigationRoot = Tree.Root.Children.FirstOrDefault(node => node.Object.Kind == MySqlObjectKind.Database && node.Object.Name == FocusedDatabase);
                // 数据库列表可能因权限或分页缺少目标；已知名称仍可按权限直接打开。
                if (NavigationRoot is null)
                {
                    NavigationRoot = new(new(MySqlObjectKind.Database, FocusedDatabase, FocusedDatabase), Tree) { Parent = Tree.Root };
                    Tree.Root.Children.Add(NavigationRoot);
                }
                await Tree.LoadAsync(NavigationRoot).WaitAsync(linked.Token);
            }
            linked.Token.ThrowIfCancellationRequested();
            if (!session.IsConnected) throw Text.Exception("MySqlConnectionFailed");
            HasConnectedOnce = true;
            if (Tabs.Count == 0) NewQuery();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { Tree?.Dispose(); Tree = null; if (_metadata is not null) await _metadata.DisposeAsync(); _metadata = null; }
        catch (Exception error)
        { connectionError = Text.Error(error, "MySqlConnectionFailed"); Tree?.Dispose(); Tree = null; if (_metadata is not null) await _metadata.DisposeAsync(); _metadata = null; }
        finally
        {
            IsConnecting = false; Connection.Finish(IsClosed || linked.IsCancellationRequested ? "" : connectionError); Refresh();
            if (IsConnected && !IsClosed) StartConnectionWatch(_metadata!);
        }
    }
    /// <summary>创建独立 SQL 文稿和延迟会话。</summary>
    public MySqlTabViewModel? NewQuery(string? database = null)
    {
        if (_options is null || !IsConnected || IsClosed) return null;
        var tab = CreateTab(Text.Format("MySqlQueryTitle", ++_queryNumber), database ?? SelectedDatabase(), MySqlObjectKind.Root);
        Tabs.Add(tab); ActiveTab = tab; return tab;
    }
    private string SelectedDatabase()
    {
        for (var node = SelectedNode; node is not null; node = node.Parent)
        {
            if (node.Object.Database.Length > 0) return node.Object.Database;
            if (node.Object.Kind == MySqlObjectKind.Database) return node.Object.Name;
        }
        return IsDatabaseFocused ? FocusedDatabase : ActiveTab?.Database is { Length: > 0 } active ? active : _metadata?.Options.Database ?? "";
    }
    private MySqlTabViewModel CreateTab(string name, string database, MySqlObjectKind kind)
    {
        var tab = new MySqlTabViewModel(name, database, kind, _factory, _options!, _dialogs, _lifetime.Token, _instance)
        { Databases = Tree?.Root.Children.Where(node => !node.IsPlaceholder).Select(node => node.Object.Name).ToArray() ?? [] };
        tab.PropertyChanged += OnTabChanged; return tab;
    }
    /// <summary>创建独立连接，不使用池化或自动重试。</summary>
    public async Task OpenAsync(MySqlTreeNode node)
    {
        if (_options is null || !IsConnected || IsClosed || node.IsPlaceholder) return;
        var item = node.Object;
        if (item.Kind is MySqlObjectKind.Database) { OpenDatabase(item.Name); return; }
        bool structure = false;
        while (node.Object.Kind is not (MySqlObjectKind.Table or MySqlObjectKind.View or MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Event))
        {
            if (node.Parent is null || node.Object.Kind is MySqlObjectKind.Tables or MySqlObjectKind.Views or MySqlObjectKind.Procedures or MySqlObjectKind.Functions or MySqlObjectKind.Events) { node.IsExpanded = true; return; }
            structure = true; node = node.Parent;
        }
        await OpenObjectAsync(node.Object, structure);
    }
    /// <summary>按对象身份去重或激活页签。</summary>
    public async Task OpenObjectAsync(MySqlObject item, bool structure = false)
    {
        if (_options is null || !IsConnected || IsClosed) return;
        var tab = Tabs.FirstOrDefault(tab => tab.Kind == item.Kind && tab.Database == item.Database && tab.Name == item.Name);
        if (tab is null) { tab = CreateTab(item.Name, item.Database, item.Kind); Tabs.Add(tab); ActiveTab = tab; await tab.ReadAsync(); }
        ActiveTab = tab; if (structure) tab.Section = "structure";
    }
    /// <summary>点击数据库请求一个独立聚焦窗口，重复请求由外壳激活已有窗口。</summary>
    public void OpenDatabase(string database)
    {
        if (!IsConnected || IsConnecting || IsClosed || string.IsNullOrWhiteSpace(database)) return;
        RequestWorkspace("mysql.database:" + database, () => CreateWorkspace(database));
    }
    private MySqlWorkspaceViewModel CreateWorkspace(string? database) => new(_instance, _configurations, _sessionFactory, _editor, _dialogs, database, _agentClient);
    private async Task CloseTabAsync(MySqlTabViewModel? tab)
    {
        if (tab is null || !Tabs.Contains(tab) || !await tab.RequestCloseAsync()) return;
        int index = Tabs.IndexOf(tab);
        // ListBox 删除选中项会同步回写空值，必须在移除前选定相邻页签。
        if (ReferenceEquals(ActiveTab, tab))
        {
            ActiveTab = Tabs.Count == 1 ? null : Tabs[index + 1 < Tabs.Count ? index + 1 : index - 1];
            if (ReferenceEquals(ActiveTab, tab)) return;
        }
        tab.PropertyChanged -= OnTabChanged; Tabs.Remove(tab); await tab.DisposeAsync();
    }
    private async Task RefreshTreeAsync()
    {
        if (Tree is null) return;
        var node = SelectedNode; while (node is not null && !node.CanExpand) node = node.Parent;
        await Tree.RefreshAsync(node ?? NavigationRoot);
        foreach (var tab in Tabs) tab.Databases = Tree.Root.Children.Where(item => !item.IsPlaceholder).Select(item => item.Object.Name).ToArray();
        Refresh();
    }
    private async Task ReconnectAsync()
    {
        var plans = new List<WorkspaceClosePlan>();
        foreach (var tab in Tabs.ToArray())
        { var plan = await tab.PrepareReconnectAsync(); if (plan is null) return; plans.Add(plan); }
        foreach (var plan in plans) await plan.CommitAsync();
        foreach (var tab in Tabs) await tab.DisconnectSessionAsync();
        await ConnectAsync();
        if (IsConnected && _options is not null)
            foreach (var tab in Tabs) tab.RebindConnection(_options);
    }
    private async Task EditConnectionAsync()
    {
        try
        {
            if (await _editor.EditAsync(InstanceId!) is { } updated) { _instance.UpdateSnapshot(updated); UpdateInstanceMetadata(updated); Error = Text.Get("MySqlReconnectToApply"); Refresh(); }
        }
        catch (Exception error) { Error = Text.Error(error, "MySqlConfigurationInvalid"); Refresh(); }
    }
    private async Task OpenQueryAsync()
    {
        try { if (await _dialogs.OpenQueryAsync(_lifetime.Token) is { } sql && NewQuery() is { } tab) tab.Sql = sql; }
        catch (Exception error) { Error = Text.Error(error, "MySqlFileFailed"); Refresh(); }
    }
    private void OnTabChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(MySqlTabViewModel.HasResult) or nameof(MySqlTabViewModel.IsBusy)) ExportCommand.NotifyCanExecuteChanged(); }
    /// <summary>确认正在执行的命令、数据草稿、文稿和事务后才关闭。</summary>
    public override async Task<bool> RequestCloseAsync() { foreach (var tab in Tabs.ToArray()) if (!await tab.RequestCloseAsync()) return false; return true; }
    /// <summary>跨窗口退出不提前停止其他查询、丢弃数据草稿或回滚事务。</summary>
    public override async Task<WorkspaceClosePlan?> PrepareCloseAsync()
    {
        var plans = new List<WorkspaceClosePlan>();
        foreach (var tab in Tabs.ToArray())
        {
            var plan = await tab.PrepareCloseAsync();
            if (plan is null) return null;
            plans.Add(plan);
        }
        return new WorkspaceClosePlan(async () => { foreach (var plan in plans) await plan.CommitAsync(); });
    }
    protected override async ValueTask ReleaseAsync()
    {
        ReleaseAssistant(); _lifetime.Cancel(); _factory.Reset(); Connection.Finish(); await StopConnectionWatchAsync(); Tree?.Dispose();
        foreach (var tab in Tabs) { tab.PropertyChanged -= OnTabChanged; await tab.DisposeAsync(); }
        Tabs.Clear(); if (_metadata is not null) await _metadata.DisposeAsync(); _lifetime.Dispose();
    }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(Endpoint), nameof(WorkspaceSubtitle), nameof(IsConnecting), nameof(IsConnected), nameof(Status), nameof(ConnectionTone), nameof(Error), nameof(HasError), nameof(Tree), nameof(NavigationRoot) }) OnPropertyChanged(property);
        NewQueryCommand.NotifyCanExecuteChanged(); NewCrossDatabaseQueryCommand.NotifyCanExecuteChanged(); OpenQueryCommand.NotifyCanExecuteChanged(); RefreshTreeCommand.NotifyCanExecuteChanged(); ReconnectCommand.NotifyCanExecuteChanged(); EditConnectionCommand.NotifyCanExecuteChanged();
    }
    partial void InitializeAssistant(IAgentClient? client);
    partial void ReleaseAssistant();
    partial void OnActiveTabChanged();
}
