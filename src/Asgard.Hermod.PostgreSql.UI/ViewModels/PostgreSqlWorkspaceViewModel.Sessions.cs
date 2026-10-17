using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

public sealed partial class PostgreSqlWorkspaceViewModel
{
    /// <summary>目录检索不改变用户的勾选。</summary>
    public string CatalogSearch { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(CatalogItems)); } } = "";
    /// <summary>只过滤显示，不代替用户选择。</summary>
    public IReadOnlyList<DatabaseChoiceViewModel> CatalogItems => DatabaseChoices.Where(item => item.Name.Contains(CatalogSearch, StringComparison.OrdinalIgnoreCase)).ToArray();
    /// <summary>唯一数据库计入直接使用，多库只统计用户的明确勾选。</summary>
    public string CatalogSelectionSummary => DatabaseChoices.Any(item => HasSingleCatalogDatabase || item.IsSelected)
        ? Text.Format("DatabaseSessionSelectedCount", DatabaseChoices.Count(item => HasSingleCatalogDatabase || item.IsSelected)) : Text.Get("DatabasePickNone");
    /// <summary>真实数据库名称保持原文，便于核对即将打开的范围。</summary>
    public string CatalogSelectionNames => string.Join(Environment.NewLine, DatabaseChoices.Where(item => HasSingleCatalogDatabase || item.IsSelected).Select(item => item.Name));
    private IReadOnlyList<DatabaseIdentity> InitialDatabases { get; }
    /// <summary>实例目录与编辑 Session 使用不同内容布局。</summary>
    public bool IsCatalog => !IsDatabaseFocused;
    /// <summary>当前窗口的独立脚本 Session。</summary>
    public ObservableCollection<PostgreSqlTabViewModel> Sessions { get; } = [];
    /// <summary>对象视图始终回溯到所属脚本 Session。</summary>
    public PostgreSqlTabViewModel? ActiveSession { get => ActiveTab is { IsQuery: true } ? ActiveTab : ActiveTab?.SessionOwner; set { if (value is not null && Sessions.Contains(value) && value != ActiveTab) ActiveTab = value; } }
    /// <summary>只呈现当前 Session 的脚本和对象视图。</summary>
    public IReadOnlyList<PostgreSqlTabViewModel> ActiveDocuments => ActiveSession?.Documents ?? (IReadOnlyList<PostgreSqlTabViewModel>)Array.Empty<PostgreSqlTabViewModel>();
    /// <summary>左侧只展示当前 Session 纳入的数据库。</summary>
    public IReadOnlyList<PostgreSqlTreeNode> SessionTreeItems => _sessionTreeItems;
    private IReadOnlyList<PostgreSqlTreeNode> _sessionTreeItems = [];
    /// <summary>目录的明确选择；唯一数据库直接使用。</summary>
    public ObservableCollection<DatabaseChoiceViewModel> DatabaseChoices { get; } = [];
    /// <summary>只有一个已知库且目录已完整加载时省去选择。</summary>
    public bool HasSingleCatalogDatabase => DatabaseChoices.Count == 1 && Tree?.Root.HasMore == false;
    /// <summary>多库目录等待用户选择。</summary>
    public bool HasMultipleCatalogDatabases => !HasSingleCatalogDatabase;
    /// <summary>串行保护范围确认，防止迟到弹窗作用于新的 Session。</summary>
    public bool IsManagingDatabases { get; private set; }
    /// <summary>在当前实例建立包含所选数据库的编辑 Session。</summary>
    public AsyncRelayCommand OpenSessionCommand { get; private set; } = null!;
    /// <summary>用户主动全选目录中的已加载数据库。</summary>
    public AsyncRelayCommand SelectAllDatabasesCommand { get; private set; } = null!;
    /// <summary>同实例追加数据库，保留已有执行目标。</summary>
    public AsyncRelayCommand AddSessionDatabaseCommand { get; private set; } = null!;
    /// <summary>最后一个数据库不可移除。</summary>
    public AsyncRelayCommand RemoveSessionDatabaseCommand { get; private set; } = null!;
    /// <summary>在所属脚本 Session 查看执行历史，不聚合其他 Session。</summary>
    public RelayCommand ShowSessionHistoryCommand { get; private set; } = null!;
    /// <summary>返回当前对象所属的脚本。</summary>
    public RelayCommand ReturnToScriptCommand { get; private set; } = null!;
    /// <summary>全选状态只涵盖已经完整取得的当前实例目录。</summary>
    public bool IsAllDatabasesSelected => DatabaseChoices.Count > 0 && Tree?.Root.HasMore == false && DatabaseChoices.All(item => item.IsSelected);
    /// <summary>同一个全选入口可撤销选择，读取分页失败时不改变选择。</summary>
    public AsyncRelayCommand ToggleAllDatabasesCommand { get; private set; } = null!;
    /// <summary>范围入口使用完整翻译模板，计数与语言无关。</summary>
    public string DatabaseScopeLabel => Text.Format("DatabaseScopeLabel", ActiveSession?.Scope?.Databases.Count ?? 0);
    private void InitializeSessionCommands()
    {
        OpenScopeEditorCommand = new AsyncRelayCommand(_ => OpenScopeEditorAsync(), _ => IsConnected && !IsManagingDatabases && ActiveSession is { IsBusy: false, IsManagingScope: false });
        ToggleAllDatabasesCommand = new AsyncRelayCommand(async _ => { bool selected = IsAllDatabasesSelected; try { if (await LoadDatabaseCatalogAsync()) foreach (var item in DatabaseChoices) item.IsSelected = !selected; } finally { OnPropertyChanged(nameof(IsAllDatabasesSelected)); } }, _ => IsConnected && !IsManagingDatabases);
        CancelDatabasePickerCommand = new RelayCommand(_ => { if (Sessions.Count == 0) CloseWindowCommand.Execute(null); else SetDatabasePicker(false); }, _ => !IsClosed);
        ShowSessionHistoryCommand = new RelayCommand(_ => { if (ActiveSession is { } session) { ActiveTab = session; session.IsHistoryOpen = !session.IsHistoryOpen; } }, _ => ActiveSession is not null);
        ReturnToScriptCommand = new RelayCommand(_ => ActiveTab = ActiveSession, _ => ActiveSession is not null && ActiveTab != ActiveSession);
        OpenSessionCommand = new AsyncRelayCommand(_ => OpenSelectedSessionAsync(), _ => IsConnected && !IsManagingDatabases && (HasSingleCatalogDatabase || DatabaseChoices.Any(item => item.IsSelected)));
        SelectAllDatabasesCommand = new AsyncRelayCommand(async _ => { if (await LoadDatabaseCatalogAsync()) foreach (var item in DatabaseChoices) item.IsSelected = true; }, _ => IsConnected && !IsManagingDatabases);
        AddSessionDatabaseCommand = new AsyncRelayCommand(_ => AddSessionDatabasesAsync(), _ => IsConnected && !IsManagingDatabases && ActiveSession is { IsBusy: false, IsManagingScope: false });
        RemoveSessionDatabaseCommand = new AsyncRelayCommand(item => RemoveSessionDatabaseAsync(item as DatabaseIdentity), item => IsConnected && !IsManagingDatabases && ActiveSession is { IsBusy: false, IsManagingScope: false, Scope.CanRemoveDatabase: true } && item is DatabaseIdentity);
    }
    /// <summary>用户全选或打开范围选择器时读完数据库分页，避免把第一页误当成唯一候选。</summary>
    private async Task<bool> LoadDatabaseCatalogAsync()
    {
        if (Tree is null || !IsConnected) return false;
        while (Tree.Root.HasMore && !IsClosed && IsConnected)
        {
            int offset = Tree.Root.Offset;
            await Tree.LoadAsync(Tree.Root);
            if (Tree.Root.Offset == offset)
            {
                Error = Text.Get("DatabaseSessionCatalogIncomplete"); Refresh(); return false;
            }
        }
        RefreshCatalog(); return !Tree.Root.HasMore && !IsClosed && IsConnected;
    }
    private void RefreshCatalog()
    {
        if (Tree is null) return;
        var names = Tree.Root.Children.Where(node => node.Object.Kind == PostgreSqlObjectKind.Database && !node.IsPlaceholder).Select(node => node.Object.Name).ToArray();
        foreach (var stale in DatabaseChoices.Where(item => !names.Contains(item.Name, StringComparer.Ordinal)).ToArray()) DatabaseChoices.Remove(stale);
        foreach (string name in names.Where(name => DatabaseChoices.All(item => item.Name != name)))
        {
            var item = new DatabaseChoiceViewModel(new(InstanceId!, name));
            item.PropertyChanged += (_, _) => { RefreshCatalogSelection(); }; DatabaseChoices.Add(item);
        }
        OnPropertyChanged(nameof(CatalogItems)); OnPropertyChanged(nameof(HasSingleCatalogDatabase)); OnPropertyChanged(nameof(HasMultipleCatalogDatabases)); RefreshCatalogSelection();
    }
    private void RefreshCatalogSelection()
    {
        OnPropertyChanged(nameof(IsAllDatabasesSelected)); OnPropertyChanged(nameof(CatalogSelectionSummary)); OnPropertyChanged(nameof(CatalogSelectionNames));
        OpenSessionCommand.NotifyCanExecuteChanged(); NewQueryCommand.NotifyCanExecuteChanged();
    }
    /// <summary>仅统计当前 Session 范围中真正已加载的对象。</summary>
    public string NavigationLoadedSummary
    {
        get
        {
            int tables = 0, views = 0;
            foreach (var database in SessionTreeItems) Count(database);
            return tables + views == 0 ? "" : Text.Format("DatabaseLoadedObjects", tables, views);
            void Count(PostgreSqlTreeNode node)
            {
                if (!node.IsPlaceholder && node.Object.Kind == PostgreSqlObjectKind.Table) tables++;
                if (!node.IsPlaceholder && node.Object.Kind == PostgreSqlObjectKind.View) views++;
                foreach (var child in node.Children) Count(child);
            }
        }
    }
    private void RefreshSessionNavigation()
    {
        if (ScopeEditor is not null && (_scopeEditorSession != ActiveSession || !ScopeEditor.IsCurrent)) ScopeEditor.IsOpen = false;
        OpenScopeEditorCommand?.NotifyCanExecuteChanged();
        if (Tree is not null && ActiveSession?.Scope is { } scope)
            foreach (var database in scope.Databases.Where(item => Tree.Root.Children.All(node => node.Object.Name != item.Name)))
                Tree.Root.Children.Add(new(new(PostgreSqlObjectKind.Database, database.Name, database.Name), Tree) { Parent = Tree.Root });
        // SQL 和对象文档之间切换不替换同一个范围的树数据源，保留原生容器、选中项和滚动位置。
        var nodes = Tree?.Root.Children.Where(node => ActiveSession?.Scope?.Contains(node.Object.Name) == true).ToArray() ?? [];
        if (!_sessionTreeItems.SequenceEqual(nodes)) { _sessionTreeItems = nodes; OnPropertyChanged(nameof(SessionTreeItems)); }
        foreach (string name in new[] { nameof(ActiveSession), nameof(ActiveDocuments), nameof(NavigationLoadedSummary), nameof(DatabaseScopeLabel) }) OnPropertyChanged(name);
        ToggleAllDatabasesCommand.NotifyCanExecuteChanged(); SelectAllDatabasesCommand.NotifyCanExecuteChanged(); ToggleAssistantCommand?.NotifyCanExecuteChanged(); ShowSessionHistoryCommand.NotifyCanExecuteChanged(); ReturnToScriptCommand.NotifyCanExecuteChanged();
        AddSessionDatabaseCommand.NotifyCanExecuteChanged(); RemoveSessionDatabaseCommand.NotifyCanExecuteChanged();
    }
    private bool _selectingDatabases;
    /// <summary>取消新 Session 的选库只回到原会话，不修改其范围与执行目标。</summary>
    public RelayCommand CancelDatabasePickerCommand { get; private set; } = null!;
    /// <summary>已有会话时才显示返回入口。</summary>
    public bool CanCancelDatabasePicker => !IsClosed;
    private void SetDatabasePicker(bool selecting)
    {
        _selectingDatabases = selecting;
        foreach (string name in new[] { nameof(IsCatalog), nameof(IsDatabaseFocused), nameof(CanCancelDatabasePicker), nameof(NavigationTitle) }) OnPropertyChanged(name);
        RefreshSessionNavigation();
        CancelDatabasePickerCommand.NotifyCanExecuteChanged();
    }
    private Task OpenSelectedSessionAsync()
    {
        var selected = DatabaseChoices.Where(item => HasSingleCatalogDatabase || item.IsSelected).Select(item => item.Identity).ToArray();
        if (selected.Length > 0) CreateSession(selected);
        return Task.CompletedTask;
    }
    private async Task StartSessionAsync()
    {
        if (!await LoadDatabaseCatalogAsync()) return;
        var candidates = DatabaseChoices.Select(item => item.Identity).ToArray();
        if (candidates.Length == 1 && Tree?.Root.HasMore == false) { CreateSession(candidates); return; }
        foreach (var choice in DatabaseChoices) choice.IsSelected = false;
        SetDatabasePicker(true);
    }
    private PostgreSqlTabViewModel? CreateSession(IReadOnlyList<DatabaseIdentity> databases)
    {
        if (_options is null || databases.Count == 0) return null;
        var scope = new DatabaseSessionScope(InstanceId!, Text.Format("DatabaseSessionName", ++_queryNumber), databases);
        var tab = CreateTab(scope.SessionName, scope.ExecutionTarget, PostgreSqlObjectKind.Root);
        tab.InitializeScope(scope); tab.PropertyChanged += (_, args) => { if (args.PropertyName is nameof(tab.ScopeRevision) or nameof(tab.IsBusy)) RefreshSessionNavigation(); };
        tab.Documents.Add(tab); Sessions.Add(tab); Tabs.Add(tab); ActiveTab = tab; SetDatabasePicker(false); return tab;
    }
    private async Task AddSessionDatabasesAsync()
    {
        var tab = ActiveSession;
        if (tab?.Scope is not { } scope || IsManagingDatabases) return;
        IsManagingDatabases = true; tab.IsManagingScope = true; RefreshSessionNavigation();
        try
        {
            if (!await LoadDatabaseCatalogAsync()) return;
            var selected = await _dialogs.SelectSessionDatabasesAsync(_instance.Name, Endpoint, tab.Name,
                DatabaseChoices.Select(item => item.Identity).ToArray(), scope.Databases.ToArray());
            if (selected is { Count: > 0 } && Sessions.Contains(tab) && !IsClosed) scope.Add(selected);
        }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); }
        finally { IsManagingDatabases = false; tab.IsManagingScope = false; RefreshSessionNavigation(); Refresh(); }
    }
    /// <summary>确认和全部草稿、事务保护完成后才提交范围；取消保留原状。</summary>
    public async Task RemoveSessionDatabaseAsync(DatabaseIdentity? database)
    {
        var tab = ActiveSession;
        if (database is null || tab?.Scope is not { } scope || tab.IsBusy || tab.IsManagingScope || database.InstanceId != scope.InstanceId || IsManagingDatabases) return;
        var request = scope.PrepareRemoval(database.Name); if (request is null) return;
        IsManagingDatabases = true; tab.IsManagingScope = true; RefreshSessionNavigation();
        try
        {
            var decision = await _dialogs.ConfirmDatabaseRemovalAsync(_instance.Name, Endpoint, request);
            if (decision is null || scope.ReplacementFor(request, decision) is null || !Sessions.Contains(tab)) return;
            var objects = Tabs.Where(item => item.SessionOwner == tab && item.Database == database.Name).ToArray();
            var plans = new List<WorkspaceClosePlan>();
            foreach (var item in objects) { var plan = await item.PrepareCloseAsync(); if (plan is null) return; plans.Add(plan); }
            bool changeTarget = request.Database == request.ExecutionTarget;
            if (changeTarget) { var plan = await tab.PrepareReconnectAsync(); if (plan is null) return; plans.Add(plan); }
            if (!scope.Matches(request) || IsClosed) return;
            foreach (var plan in plans) await plan.CommitAsync();
            if (changeTarget) { await tab.DisconnectSessionAsync(); tab.RebindConnection(_options!); }
            if (objects.Contains(ActiveTab)) ActiveTab = tab;
            foreach (var item in objects) { item.PropertyChanged -= OnTabChanged; Tabs.Remove(item); tab.Documents.Remove(item); await item.DisposeAsync(); }
            tab.CommitScopeRemoval(request, decision);
        }
        catch (Exception error) { Error = Text.Error(error, "PostgreSqlCommandFailed"); }
        finally { IsManagingDatabases = false; tab.IsManagingScope = false; RefreshSessionNavigation(); Refresh(); }
    }
}
