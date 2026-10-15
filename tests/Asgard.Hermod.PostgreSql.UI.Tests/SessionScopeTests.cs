using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>目录、Session、AI 和移除确认使用同一套规则。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SessionScopeTests(WpfTestFixture fixture)
{
    [Fact]
    public Task Catalog_SingleDatabaseOpensDirectlyWithoutEditorInCatalog() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql();
        await using var catalog = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestPostgreSqlDialogs());
        await catalog.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.True(catalog.IsCatalog); Assert.Null(catalog.ActiveTab); Assert.True(catalog.HasSingleCatalogDatabase);
        Assert.True(catalog.OpenSessionCommand.CanExecute(null)); Assert.False(catalog.DatabaseChoices[0].IsSelected);
        WorkspacePageRequestEventArgs? requested = null; catalog.WorkspaceRequested += (_, request) => requested = request;
        await catalog.OpenSessionCommand.ExecuteAsync();
        await using var child = Assert.IsType<PostgreSqlWorkspaceViewModel>(requested!.CreatePage()); await child.InitializeAsync();
        Assert.Equal("app", child.ActiveSession!.Database); Assert.Single(child.ActiveSession.Scope!.Databases);
    });

    [Fact]
    public Task Catalog_MultipleDatabasesWaitForExplicitSelectionAndAllowSelectAll() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql { DatabaseNames = ["app", "reports"] };
        await using var catalog = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestPostgreSqlDialogs());
        await catalog.InitializeAsync(); Assert.False(catalog.HasSingleCatalogDatabase); Assert.False(catalog.OpenSessionCommand.CanExecute(null));
        catalog.SelectAllDatabasesCommand.Execute(null); Assert.All(catalog.DatabaseChoices, item => Assert.True(item.IsSelected));
        WorkspacePageRequestEventArgs? requested = null; catalog.WorkspaceRequested += (_, request) => requested = request;
        await catalog.OpenSessionCommand.ExecuteAsync();
        await using var child = Assert.IsType<PostgreSqlWorkspaceViewModel>(requested!.CreatePage()); await child.InitializeAsync();
        Assert.Equal(2, child.ActiveSession!.Scope!.Databases.Count); Assert.Empty(child.ActiveSession.Database); Assert.False(child.ActiveSession.CanRun);
        await child.ActiveSession.ExecuteAsync("SELECT 1"); Assert.Equal(0, server.Executions);
        await child.ActiveSession.ChangeDatabaseAsync("reports"); Assert.True(child.ActiveSession.CanRun);
        await child.ActiveSession.ChangeDatabaseAsync("outside"); Assert.Equal("reports", child.ActiveSession.Database);
    });

    [Fact]
    public Task Removal_CancelKeepsScopeAndLastDatabaseCannotBeRemoved() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestPostgreSqlDialogs(); var server = new TestPostgreSql();
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); session.Sql = "SELECT pending";
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Equal(2, session.Scope!.Databases.Count); Assert.Equal("app", session.Database); Assert.Equal("SELECT pending", session.Sql);
        dialogs.RemovalDecision = new(); await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Single(session.Scope.Databases); Assert.Equal("reports", session.Database); Assert.Equal("SELECT pending", session.Sql);
        Assert.Equal(0, server.Executions); int confirmations = dialogs.RemovalConfirmations;
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "reports")); Assert.Equal(confirmations, dialogs.RemovalConfirmations);
    });

    [Fact]
    public Task Removal_MultipleTargetsAndForeignInstancesAreNeverGuessed() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestPostgreSqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, new TestPostgreSql(), dialogs, "app", "reports", "audit"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app");
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app")); Assert.Equal(3, session.Scope!.Databases.Count);
        await page.RemoveSessionDatabaseAsync(new("another-instance", "app")); Assert.Equal(1, dialogs.RemovalConfirmations);
        dialogs.RemovalDecision = new("audit"); await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Equal("audit", session.Database); Assert.Equal(2, session.Scope.Databases.Count);
    });

    [Fact]
    public Task Ai_ReadsOnlySessionDatabasesAndRejectsSuggestionsAfterScopeChange() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestPostgreSqlDialogs(); var server = new TestPostgreSql();
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); server.MetadataDatabases.Clear();
        var context = await session.ReadAssistantContextAsync(true, false, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "app", "reports" }, context.Databases); Assert.Empty(context.Results);
        Assert.All(server.MetadataDatabases.Where(name => name.Length > 0), name => Assert.Contains(name, context.Databases!));
        using var assistant = new PostgreSqlAssistantViewModel(session, new ReplyClient()) { Prompt = "show tables" };
        await assistant.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(assistant.Messages.Last().SqlBlocks);
        dialogs.SelectedDatabases = [new(Configurations.Instance.Id, "audit")]; await page.AddSessionDatabaseCommand.ExecuteAsync();
        await assistant.ExecuteCommand.ExecuteAsync(suggestion); Assert.Equal(0, server.Executions);
        dialogs.ActionAllowed = false; await assistant.ClearCommand.ExecuteAsync(); Assert.Equal(2, assistant.Messages.Count);
        dialogs.ActionAllowed = true; await assistant.ClearCommand.ExecuteAsync(); Assert.Empty(assistant.Messages);
    });

    [Fact]
    public Task ObjectViewsShareOwnerAiAndClosingCleanSessionStillNeedsConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestPostgreSqlDialogs { ActionAllowed = false };
        await using var page = Page(config, new TestPostgreSql(), dialogs, "app"); await page.InitializeAsync();
        var session = page.ActiveSession!; var assistant = page.Assistant;
        await page.OpenObjectAsync(new(PostgreSqlObjectKind.Table, "users", "app"));
        Assert.Same(session, page.ActiveSession); Assert.Same(assistant, page.Assistant); Assert.Equal(2, page.ActiveDocuments.Count);
        await page.CloseTabCommand.ExecuteAsync(session); Assert.Equal(2, page.Tabs.Count);
        dialogs.ActionAllowed = true; await page.CloseTabCommand.ExecuteAsync(session); Assert.Empty(page.Tabs); Assert.Empty(page.Sessions);
    });

    [Fact]
    public Task AiSuggestion_BecomesStaleAfterReconnectBeforeFirstExecution() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
        await using var page = Page(config, server, dialogs, "app"); await page.InitializeAsync();
        var session = page.ActiveSession!;
        using var assistant = new PostgreSqlAssistantViewModel(session, new ReplyClient()) { Prompt = "show users" };
        await assistant.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(assistant.Messages.Last().SqlBlocks);
        await page.ReconnectCommand.ExecuteAsync();
        await session.ExecuteAssistantSqlAsync(suggestion); Assert.True(session.HasError); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task RemovedDatabaseResultsRemainVisibleButCannotEnterAiContext() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); await session.ExecuteAsync("SELECT 1");
        var result = session.SelectedResult; Assert.True(session.CanShareResults);
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Same(result, session.SelectedResult); Assert.False(session.CanShareResults);
        var context = await session.ReadAssistantContextAsync(false, true, TestContext.Current.CancellationToken);
        Assert.Empty(context.Results); Assert.Equal(1, server.Executions);
    });

    [Fact]
    public Task Removal_CancelledObjectDraftKeepsOriginalScopeAndTarget() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app");
        await page.OpenObjectAsync(new(PostgreSqlObjectKind.Table, "users", "app")); var table = page.ActiveTab!;
        var grid = table.SelectedResult!; var row = grid.Rows[0]; Assert.True(await table.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("keep this edit");
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Equal(2, session.Scope!.Databases.Count); Assert.Equal("app", session.Database); Assert.Contains(table, page.Tabs);
        Assert.True(table.HasDataDraft); Assert.Equal("keep this edit", row.Cells[1].Value); Assert.Equal(0, server.Writes); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task Add_RejectsForeignInstanceAndPreservesExistingTarget() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestPostgreSqlDialogs { SelectedDatabases = [new("other-instance", "outside")] };
        await using var page = Page(config, new TestPostgreSql(), dialogs, "app"); await page.InitializeAsync();
        await page.AddSessionDatabaseCommand.ExecuteAsync(); Assert.True(page.HasError);
        Assert.Equal("app", page.ActiveSession!.Database); Assert.Single(page.ActiveSession.Scope!.Databases);
    });

    [Fact]
    public Task SelectAll_LoadsAllDatabasePagesBeforeSelecting() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestPostgreSql { DatabaseNames = ["app", "reports", "audit"], DatabasePageSize = 1 };
        await using var catalog = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestPostgreSqlDialogs());
        await catalog.InitializeAsync(); Assert.Single(catalog.DatabaseChoices); Assert.False(catalog.HasSingleCatalogDatabase);
        Assert.False(catalog.DatabaseChoices[0].IsSelected);
        await catalog.SelectAllDatabasesCommand.ExecuteAsync(); Assert.Equal(3, catalog.DatabaseChoices.Count);
        Assert.All(catalog.DatabaseChoices, item => Assert.True(item.IsSelected)); Assert.True(catalog.OpenSessionCommand.CanExecute(null));
    });

    /// <summary>实际 WPF 窗体校验目录、范围条、对象树和 AI 的中英文布局。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 1440)]
    [InlineData("zh-Hans", UiTheme.Dark, 1120)]
    public Task Layout_CatalogAndSessionRespectScope(string language, UiTheme theme, int width) => fixture.RunAsync(async () =>
    {
        using var trace = new WpfBindingTrace();
        Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(System.Globalization.CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var config = new Configurations(); var server = new TestPostgreSql { DatabaseNames = ["app", "reports", "audit"] };
        await using var catalog = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestPostgreSqlDialogs());
        await catalog.InitializeAsync();
        await using var page = Page(config, server, new TestPostgreSqlDialogs(), "app", "reports"); await page.InitializeAsync();
        page.IsAssistantOpen = true;
        var view = new PostgreSqlWorkspaceView { DataContext = catalog };
        var layout = Assert.IsType<WorkspacePageLayout>(view.FindName("WorkspaceLayout"));
        var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); layout.Toolbar = null; header.DataContext = catalog; header.ShowWindowControls = true;
        var window = new HermodWindow { Content = view, CaptionContent = header, CaptionHeight = 72, Width = width, Height = 900 };
        try
        {
            WindowPreviewCapture.Show(window); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(Assert.IsType<Grid>(view.FindName("Body")).IsVisible);
            await WindowPreviewCapture.VerifyAsync(window, "postgresql-catalog-" + language);
            view.DataContext = page; header.DataContext = page; await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(Assert.IsType<Grid>(view.FindName("Body")).IsVisible);
            var tree = Assert.IsType<WorkspaceTreeView>(view.FindName("ObjectTree"));
            Assert.Equal(2, tree.Items.Count); Assert.All(tree.Items.Cast<PostgreSqlTreeNode>(), node => Assert.Contains(node.Object.Name, new[] { "app", "reports" }));
            var host = Assert.IsType<ContentControl>(view.FindName("TabHost"));
            Assert.Same(page.ActiveSession, Assert.IsType<PostgreSqlTabView>(host.Content).DataContext);
            await WindowPreviewCapture.VerifyAsync(window, "postgresql-session-" + language);
            var split = Assert.IsType<WorkspaceSplitView>(view.FindName("AgentSplit"));
            var assistantView = Assert.IsType<PostgreSqlAssistantView>(split.PaneContent);
            var assistant = page.Assistant!; assistant.Prompt = "keep this draft";
            assistant.Messages.Add(new("keep this conversation", true));
            var script = page.ActiveSession!; script.Sql = "SELECT 42;";
            Assert.True(split.IsPaneOpen); Assert.Equal(6, split.ColumnDefinitions[1].ActualWidth);
            var main = Assert.IsType<Grid>(split.MainContent);
            Assert.True(main.ActualWidth <= assistantView.TranslatePoint(new(), split).X);
            window.Width = 1440; await WindowTestVisuals.UpdateLayoutAsync(window);
            split.ColumnDefinitions[2].Width = new GridLength(480); await WindowTestVisuals.UpdateLayoutAsync(window);
            split.Children.OfType<GridSplitter>().Single().RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0, 0, false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            Assert.InRange(page.AssistantWidth, 479, 481);
            Assert.IsType<Button>(Assert.IsType<AgentPanel>(assistantView.FindName("ChatPanel")).FindName("HideAgentButton")).Command!.Execute(null);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.False(split.IsPaneOpen); Assert.False(assistantView.IsVisible);
            Assert.Equal(0, split.ColumnDefinitions[2].ActualWidth);
            var treePanel = Assert.IsType<Border>(view.FindName("TreePanel")); Assert.True(treePanel.IsVisible);
            var node = page.SessionTreeItems.First(); node.IsExpanded = true; page.SelectedNode = node;
            double treeWidth = treePanel.ActualWidth;
            Assert.IsType<Button>(view.FindName("CollapseTreeButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await WindowTestVisuals.UpdateLayoutAsync(window); Assert.False(treePanel.IsVisible);
            Assert.Equal(0, Assert.IsType<ColumnDefinition>(view.FindName("TreeColumn")).ActualWidth);
            var restoreTree = Assert.IsType<Button>(view.FindName("DrawerButton")); Assert.True(restoreTree.IsVisible);
            await WindowPreviewCapture.VerifyAsync(window, "postgresql-panels-collapsed-" + language);
            restoreTree.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(treePanel.IsVisible); Assert.InRange(treePanel.ActualWidth, treeWidth - 1, treeWidth + 1);
            Assert.True(node.IsExpanded); Assert.Same(node, page.SelectedNode);
            page.ToggleAssistantCommand.Execute(null); await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.True(assistantView.IsVisible); Assert.InRange(split.ColumnDefinitions[2].ActualWidth, 479, 481);
            Assert.Same(assistant, assistantView.DataContext); Assert.Equal("keep this draft", assistant.Prompt);
            Assert.Single(assistant.Messages); Assert.Same(script, page.ActiveSession); Assert.Equal("SELECT 42;", script.Sql);
            var actions = Assert.IsType<StackPanel>(header.ActionsContent); Assert.Equal(3, actions.Children.Count);
            Assert.Same(page.EditConnectionCommand, Assert.IsType<Button>(actions.Children[0]).Command);
            Assert.IsType<WorkspaceTools>(actions.Children[1]); Assert.Same(page.ToggleAssistantCommand, Assert.IsType<Button>(actions.Children[2]).Command);
            foreach (double captionWidth in new[] { 800d, 1120d, 1440d })
            {
                window.Width = captionWidth; await WindowTestVisuals.UpdateLayoutAsync(window);
                var controls = WindowTestVisuals.Descendants<WindowControls>(header).Single(); double controlsLeft = controls.TranslatePoint(new(), header).X;
                foreach (FrameworkElement action in actions.Children) Assert.True(action.TranslatePoint(new(), header).X + action.ActualWidth <= controlsLeft);
            }
            Assert.Empty(trace.Messages);
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(null); }
    });

    private static PostgreSqlWorkspaceViewModel Page(Configurations config, TestPostgreSql server, TestPostgreSqlDialogs dialogs, params string[] databases) =>
        new(new(Configurations.Instance), config, server, config, dialogs, initialDatabases: databases.Select(name => new DatabaseIdentity(Configurations.Instance.Id, name)).ToArray());
    private sealed class ReplyClient : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => Task.FromResult(AgentReply.FromContent("```sql\nSELECT 3;\n```"));
    }
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("engine", "Database", "Database", "database", [], TypeId: PostgreSqlIds.Type, ProviderId: PostgreSqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestPostgreSql.Connection);
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
