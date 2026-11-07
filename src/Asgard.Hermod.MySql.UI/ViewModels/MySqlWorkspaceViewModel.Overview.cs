namespace Asgard.Hermod.MySql.UI.ViewModels;

public sealed partial class MySqlWorkspaceViewModel
{
    /// <summary>重新打开当前 Session 的概要，不改变对象和脚本文档。</summary>
    public RelayCommand ShowOverviewCommand { get; private set; } = null!;
    /// <summary>按需显示当前 Session 的 SQL 文稿。</summary>
    public RelayCommand ShowScriptCommand { get; private set; } = null!;
    /// <summary>重新读取概要正在展示的分组，不依赖树的当前选中项。</summary>
    public AsyncRelayCommand RefreshOverviewCommand { get; private set; } = null!;

    private void InitializeOverviewCommands()
    {
        ShowOverviewCommand = new RelayCommand(_ => ShowOverview(), _ => ActiveSession is not null && !IsManagingDatabases);
        ShowScriptCommand = new RelayCommand(_ => { if (ActiveSession is { } session) ShowScript(session); }, _ => ActiveSession is not null && !IsManagingDatabases);
        RefreshOverviewCommand = new AsyncRelayCommand(_ => RefreshOverviewAsync(), _ => IsConnected && !IsConnecting && ActiveSession?.OverviewCategory is not null);
    }

    private MySqlTabViewModel CreateOverview(MySqlTabViewModel session)
    {
        var overview = new MySqlTabViewModel("", "", MySqlObjectKind.Root, _factory, _options!, _dialogs, _lifetime.Token, _instance)
        { IsOverview = true, SessionOwner = session, Endpoint = Endpoint };
        overview.PropertyChanged += OnTabChanged; Tabs.Add(overview); session.Documents.Insert(0, overview); return overview;
    }

    /// <summary>树分组和概要页签共享同一组已加载节点。</summary>
    public void ShowOverview(MySqlTreeNode? category = null)
    {
        if (ActiveSession is not { } session || IsManagingDatabases) return;
        if (category is not null && (!IsOverviewCategory(category) || session.Scope?.Contains(category.Object.Database) != true)) return;
        if (ActiveTab?.CommitInputRequested?.Invoke() == false) return;
        var overview = session.Documents.FirstOrDefault(item => item.IsOverview) ?? CreateOverview(session);
        ActiveTab = overview;
        if (ActiveTab != overview) return;
        if (category is not null) { session.OverviewCategory = category; category.IsExpanded = true; }
    }

    private void ShowScript(MySqlTabViewModel session)
    {
        if (!Sessions.Contains(session) || ActiveTab?.CommitInputRequested?.Invoke() == false) return;
        if (!session.Documents.Contains(session)) session.Documents.Add(session);
        ActiveTab = session;
    }

    private async Task InitializeOverviewAsync(MySqlTabViewModel session)
    {
        var tree = Tree;
        if (tree is null || session.Scope is not { Databases.Count: 1 } scope) return;
        try
        {
            var database = tree.Root.Children.FirstOrDefault(item => item.Object.Kind == MySqlObjectKind.Database && item.Object.Name == scope.Databases[0].Name);
            if (database is null) return;
            await tree.LoadAsync(database);
            if (Tree != tree || !Sessions.Contains(session) || session.OverviewCategory is not null || session.Scope?.Contains(database.Object.Name) != true) return;
            var category = database.Children.FirstOrDefault(item => item.Object.Kind == MySqlObjectKind.Tables);
            if (category is null) return;
            session.OverviewCategory = category; database.IsExpanded = true;
            await tree.LoadAsync(category); category.IsExpanded = true;
        }
        catch (Exception error) { if (!IsClosed) { Error = Text.Error(error, "MySqlCommandFailed"); Refresh(); } }
    }

    private async Task RefreshOverviewAsync()
    {
        var tree = Tree; var category = ActiveSession?.OverviewCategory;
        if (tree is null || category is null || category.IsLoading) return;
        await tree.RefreshAsync(category);
        RefreshSessionNavigation(); Refresh();
    }

    // 祖先分支或连接刷新会替换节点；按数据库和类型重新绑定，而不指向失效的旧集合。
    private async Task RebindOverviewCategoriesAsync()
    {
        var tree = Tree;
        if (tree is null) return;
        foreach (var session in Sessions.ToArray())
        {
            var old = session.OverviewCategory;
            if (old is null) continue;
            var database = tree.Root.Children.FirstOrDefault(item => item.Object.Kind == MySqlObjectKind.Database && item.Object.Name == old.Object.Database);
            if (database is null || session.Scope?.Contains(old.Object.Database) != true) { session.OverviewCategory = null; continue; }
            await tree.LoadAsync(database);
            if (Tree != tree || !Sessions.Contains(session) || session.OverviewCategory != old) continue;
            var current = database.Children.FirstOrDefault(item => item.Object.Kind == old.Object.Kind);
            if (ReferenceEquals(current, old)) continue;
            session.OverviewCategory = current;
            if (current is not null) { database.IsExpanded = true; await tree.LoadAsync(current); current.IsExpanded = true; }
        }
    }

    private static bool IsOverviewCategory(MySqlTreeNode node) => node.Object.Kind is MySqlObjectKind.Tables or MySqlObjectKind.Views
        or MySqlObjectKind.Procedures or MySqlObjectKind.Functions or MySqlObjectKind.Events;
}
