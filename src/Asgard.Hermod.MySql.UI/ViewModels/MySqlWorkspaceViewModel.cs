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
    }
    public string Name => Header;
    public string Endpoint { get; private set; } = "";
    public bool IsConnecting { get; private set; }
    public bool IsConnected => _metadata?.IsConnected == true;
    public string Status => Text.Get(IsConnecting ? "MySqlConnecting" : IsConnected ? "MySqlConnected" : "MySqlDisconnected");
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public MySqlObjectTree? Tree { get; private set; }
    public MySqlTreeNode? SelectedNode { get; set; }
    public string Search { get; set { if (SetProperty(ref field, value)) Tree?.Filter(value); } } = "";
    public ObservableCollection<MySqlTabViewModel> Tabs { get; } = [];
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
    public IReadOnlyList<WorkspaceTool> Tools { get; }
    public RelayCommand NewQueryCommand { get; }
    public AsyncRelayCommand CloseTabCommand { get; }
    public AsyncRelayCommand RefreshTreeCommand { get; }
    public AsyncRelayCommand ReconnectCommand { get; }
    public AsyncRelayCommand EditConnectionCommand { get; }
    public AsyncRelayCommand OpenQueryCommand { get; }
    public AsyncRelayCommand ExportCommand { get; }
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
    public MySqlTabViewModel? NewQuery(string? database = null)
    {
        if (_options is null) return null;
        var tab = CreateTab(Text.Format("MySqlQueryTitle", ++_queryNumber), database ?? _metadata?.Options.Database ?? "", MySqlObjectKind.Root);
        Tabs.Add(tab); ActiveTab = tab; return tab;
    }
    private MySqlTabViewModel CreateTab(string name, string database, MySqlObjectKind kind)
    {
        var tab = new MySqlTabViewModel(name, database, kind, _factory, _options!, _dialogs, _lifetime.Token)
        { Databases = Tree?.Root.Children.Where(node => !node.IsPlaceholder).Select(node => node.Object.Name).ToArray() ?? [] };
        tab.PropertyChanged += OnTabChanged; return tab;
    }
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
    public async Task OpenObjectAsync(MySqlObject item, bool structure = false)
    {
        if (_options is null) return;
        var tab = Tabs.FirstOrDefault(tab => tab.Kind == item.Kind && tab.Database == item.Database && tab.Name == item.Name);
        if (tab is null) { tab = CreateTab(item.Name, item.Database, item.Kind); Tabs.Add(tab); ActiveTab = tab; await tab.ReadAsync(); }
        ActiveTab = tab; if (structure) tab.Section = "structure";
    }
    private async Task CloseTabAsync(MySqlTabViewModel? tab)
    {
        if (tab is null || !await tab.RequestCloseAsync()) return;
        int index = Tabs.IndexOf(tab); tab.PropertyChanged -= OnTabChanged; Tabs.Remove(tab); await tab.DisposeAsync();
        if (ReferenceEquals(ActiveTab, tab)) ActiveTab = Tabs.Count == 0 ? null : Tabs[Math.Min(Math.Max(0, index), Tabs.Count - 1)];
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
