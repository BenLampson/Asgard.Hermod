using Asgard.Hermod.MySql.UI.Services;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>公共外壳管理实例页签，此页面管理对象树和独立的内部会话页签。</summary>
public sealed class MySqlWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly InstanceItemViewModel _instance;
    private readonly IInstanceConfigurationService _configurations;
    private readonly IMySqlSessionFactory _factory;
    private readonly IInstanceEditor _editor;
    private readonly IMySqlWorkspaceDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime = new();
    private IMySqlSession? _metadata;
    private InstanceConnectionOptions? _options;
    private bool _initialized;
    private int _queryNumber;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlWorkspaceViewModel(InstanceItemViewModel instance, IInstanceConfigurationService configurations, IMySqlSessionFactory factory,
        IInstanceEditor editor, IMySqlWorkspaceDialogs dialogs) : base("mysql:" + Guid.NewGuid().ToString("N"), instance.Name, "database", instance.Id)
    {
        _instance = instance; _configurations = configurations; _factory = factory; _editor = editor; _dialogs = dialogs;
        NewQueryCommand = new RelayCommand(_ => NewQuery(), _ => _options is not null && !IsConnecting);
        CloseTabCommand = new AsyncRelayCommand(tab => CloseTabAsync(tab as MySqlTabViewModel));
        RefreshTreeCommand = new AsyncRelayCommand(_ => RefreshTreeAsync(), _ => Tree is not null && !IsConnecting);
        ReconnectCommand = new AsyncRelayCommand(_ => ReconnectAsync(), _ => !IsConnecting);
        EditConnectionCommand = new AsyncRelayCommand(_ => EditConnectionAsync(), _ => !IsConnecting);
        OpenQueryCommand = new AsyncRelayCommand(_ => OpenQueryAsync(), _ => _options is not null && !IsConnecting);
        ExportCommand = new AsyncRelayCommand(_ => ActiveTab?.ExportAsync() ?? Task.CompletedTask, _ => ActiveTab?.HasResult == true && !ActiveTab.IsBusy);
        Tools = [new("query.new", Text.Get("MySqlNewQuery"), "file-code", NewQueryCommand), new("query.open", Text.Get("MySqlOpenQuery"), "file", OpenQueryCommand),
            new("result.export", Text.Get("MySqlExportLoaded"), "download", ExportCommand), new("tree.refresh", Text.Get("MySqlRefreshBranch"), "refresh-cw", RefreshTreeCommand),
            new("connection.reconnect", Text.Get("MySqlReconnect"), "refresh-cw", ReconnectCommand)];
        Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasTabs));
    }
    /// <summary>对象原始名称或已本地化的文稿标题。</summary>
    public string Name => Header;
    /// <summary>公共环境徽标直接绑定稳定实例元数据。</summary>
    public InstanceItemViewModel Instance => _instance;
    /// <summary>不含凭据的公开端点。</summary>
    public string Endpoint { get; private set; } = "";
    /// <summary>实例连接初始化状态。</summary>
    public bool IsConnecting { get; private set; }
    /// <summary>当前物理连接是否可用；失败后不自动重连。</summary>
    public bool IsConnected => _metadata?.IsConnected == true;
    /// <summary>读取状态或安全失败说明。</summary>
    public string Status => Text.Get(IsConnecting ? "MySqlConnecting" : IsConnected ? "MySqlConnected" : "MySqlDisconnected");
    /// <summary>可安全展示的本地化错误。</summary>
    public string Error { get; private set; } = "";
    /// <summary>当前是否有需要呈现的错误。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>此实例的分支懒加载对象树。</summary>
    public MySqlObjectTree? Tree { get; private set; }
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
            OnPropertyChanged(); ExportCommand.NotifyCanExecuteChanged();
        }
    }
    /// <summary>公共工具面板中的稳定入口。</summary>
    public IReadOnlyList<WorkspaceTool> Tools { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public RelayCommand NewQueryCommand { get; }
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
    public override async Task InitializeAsync(CancellationToken cancellationToken = default) { if (_initialized) return; _initialized = true; await ConnectAsync(cancellationToken); }
    private async Task ConnectAsync(CancellationToken token = default)
    {
        if (IsConnecting || IsClosed) return;
        IsConnecting = true; Error = ""; Refresh();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
        try
        {
            Tree?.Dispose(); Tree = null; if (_metadata is not null) await _metadata.DisposeAsync(); _metadata = null;
            _options = await _configurations.GetConnectionAsync(InstanceId!, linked.Token) ?? throw Text.Exception("MySqlConfigurationInvalid");
            var session = await _factory.OpenAsync(_options, linked.Token);
            if (IsClosed || linked.IsCancellationRequested) { await session.DisposeAsync(); return; }
            _metadata = session; Endpoint = Text.Format("MySqlEndpoint", session.Options.Host, session.Options.Port);
            Tree = new(session, _lifetime.Token); OnPropertyChanged(nameof(Tree)); await Tree.LoadAsync(Tree.Root);
            NewQuery();
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch (Exception error) { Error = Text.Error(error, "MySqlConnectionFailed"); }
        finally { IsConnecting = false; Refresh(); }
    }
    /// <summary>创建独立 SQL 文稿和延迟会话。</summary>
    public MySqlTabViewModel? NewQuery(string? database = null)
    {
        if (_options is null) return null;
        var tab = CreateTab(Text.Format("MySqlQueryTitle", ++_queryNumber), database ?? _metadata?.Options.Database ?? "", MySqlObjectKind.Root);
        Tabs.Add(tab); ActiveTab = tab; return tab;
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
        if (_options is null || node.IsPlaceholder) return;
        var item = node.Object;
        if (item.Kind is MySqlObjectKind.Database) { node.IsExpanded = true; return; }
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
        if (_options is null) return;
        var tab = Tabs.FirstOrDefault(tab => tab.Kind == item.Kind && tab.Database == item.Database && tab.Name == item.Name);
        if (tab is null) { tab = CreateTab(item.Name, item.Database, item.Kind); Tabs.Add(tab); ActiveTab = tab; await tab.ReadAsync(); }
        ActiveTab = tab; if (structure) tab.Section = "structure";
    }
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
        await Tree.RefreshAsync(node);
        foreach (var tab in Tabs) tab.Databases = Tree.Root.Children.Where(item => !item.IsPlaceholder).Select(item => item.Object.Name).ToArray();
        Refresh();
    }
    private async Task ReconnectAsync()
    {
        if (!await RequestCloseAsync()) return;
        foreach (var tab in Tabs.ToArray()) { tab.PropertyChanged -= OnTabChanged; await tab.DisposeAsync(); }
        Tabs.Clear(); ActiveTab = null; await ConnectAsync();
    }
    private async Task EditConnectionAsync()
    {
        try
        {
            if (await _editor.EditAsync(InstanceId!) is { } updated) { _instance.UpdateSnapshot(updated); Header = updated.Name; OnPropertyChanged(nameof(Name)); Error = Text.Get("MySqlReconnectToApply"); Refresh(); }
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
    protected override async ValueTask ReleaseAsync()
    {
        _lifetime.Cancel(); Tree?.Dispose();
        foreach (var tab in Tabs) { tab.PropertyChanged -= OnTabChanged; await tab.DisposeAsync(); }
        Tabs.Clear(); if (_metadata is not null) await _metadata.DisposeAsync(); _lifetime.Dispose();
    }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(Endpoint), nameof(IsConnecting), nameof(IsConnected), nameof(Status), nameof(Error), nameof(HasError), nameof(Tree) }) OnPropertyChanged(property);
        NewQueryCommand.NotifyCanExecuteChanged(); OpenQueryCommand.NotifyCanExecuteChanged(); RefreshTreeCommand.NotifyCanExecuteChanged(); ReconnectCommand.NotifyCanExecuteChanged(); EditConnectionCommand.NotifyCanExecuteChanged();
    }
}
