using Asgard.Hermod.Agent;
using Asgard.Hermod.MySql;
using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>目录、Session、AI 和移除确认使用同一套规则。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SessionScopeTests(WpfTestFixture fixture)
{
    [Fact]
    public Task UnifiedScope_CancelKeepsWholeDraftAndApplyCommitsOneRevisionWithoutExecuting() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "reports", "audit"] }; var dialogs = new TestMySqlDialogs();
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); session.Sql = "SELECT pending";
        long revision = session.Scope!.Revision;
        await page.OpenScopeEditorCommand.ExecuteAsync(); var editor = Assert.IsType<DatabaseScopeEditorViewModel>(page.ScopeEditor);
        editor.Choices.Single(item => item.Name == "reports").IsSelected = false; editor.Choices.Single(item => item.Name == "audit").IsSelected = true;
        Assert.Equal(new[] { "app", "reports" }, session.Scope.Databases.Select(item => item.Name)); Assert.True(editor.ApplyCommand.CanExecute(null));
        await editor.ApplyCommand.ExecuteAsync();
        Assert.True(editor.IsOpen); Assert.Equal(revision, session.Scope.Revision); Assert.Equal(new[] { "app", "reports" }, session.Scope.Databases.Select(item => item.Name));
        dialogs.RemovalDecision = new(); await editor.ApplyCommand.ExecuteAsync();
        Assert.False(editor.IsOpen); Assert.Equal(revision + 1, session.Scope.Revision); Assert.Equal(new[] { "app", "audit" }, session.Scope.Databases.Select(item => item.Name));
        Assert.Equal("app", session.Database); Assert.Equal("SELECT pending", session.Sql); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task UnifiedScope_LaterRemovalCancellationDoesNotPartiallyApplyEarlierConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "reports", "audit", "warehouse"] }; var dialogs = new TestMySqlDialogs();
        await using var page = Page(config, server, dialogs, "app", "reports", "audit"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); int confirmations = 0;
        dialogs.RemovalConfirmation = () => Task.FromResult<DatabaseRemovalDecision?>(++confirmations == 1 ? new() : null);
        await page.OpenScopeEditorCommand.ExecuteAsync(); var editor = page.ScopeEditor!;
        foreach (var item in editor.Choices) item.IsSelected = item.Name == "warehouse";
        long revision = session.Scope!.Revision; await editor.ApplyCommand.ExecuteAsync();
        Assert.Equal(2, confirmations); Assert.Equal(revision, session.Scope.Revision); Assert.Equal(new[] { "app", "reports", "audit" }, session.Scope.Databases.Select(item => item.Name));
        Assert.Equal("app", session.Database); Assert.True(editor.IsOpen); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task UnifiedScope_ReplacingLastDatabaseRequiresConfirmationAndRejectsStaleDraft() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "audit"] }; var dialogs = new TestMySqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, server, dialogs, "app"); await page.InitializeAsync();
        var session = page.ActiveSession!; await page.OpenScopeEditorCommand.ExecuteAsync(); var editor = page.ScopeEditor!;
        editor.Choices.Single(item => item.Name == "app").IsSelected = false;
        Assert.False(editor.ApplyCommand.CanExecute(null)); editor.Choices.Single(item => item.Name == "audit").IsSelected = true;
        await editor.ApplyCommand.ExecuteAsync(); Assert.Equal("audit", session.Database); Assert.Single(session.Scope!.Databases); Assert.Equal(1, dialogs.RemovalConfirmations);
        await page.OpenScopeEditorCommand.ExecuteAsync(); editor = page.ScopeEditor!; editor.Choices.Single(item => item.Name == "app").IsSelected = true;
        session.Scope.Add([new(Configurations.Instance.Id, "reports")]); Assert.False(editor.ApplyCommand.CanExecute(null)); Assert.False(editor.IsOpen);
        Assert.Equal(new[] { "audit", "reports" }, session.Scope.Databases.Select(item => item.Name)); Assert.Equal(0, server.Executions);
    });


    [Fact]
    public Task HistoryRestore_ConfirmsDraftAndDoesNotExecute() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs();
        await using var tab = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, dialogs);
        tab.Sql = "SELECT original"; await tab.ExecuteAsync(tab.Sql); int calls = server.Executions;
        tab.Sql = "SELECT pending"; tab.OutputView = "history";
        await tab.RestoreTraceCommand.ExecuteAsync(); Assert.Equal("SELECT pending", tab.Sql);
        dialogs.Choice = MySqlDraftChoice.Discard; await tab.RestoreTraceCommand.ExecuteAsync();
        Assert.Equal("SELECT original", tab.Sql); Assert.Equal(calls, server.Executions); Assert.Equal("history", tab.OutputView);
    });

    [Fact]
    public Task Catalog_SingleDatabaseOpensDirectlyWithoutEditorInCatalog() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql();
        await using var catalog = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs());
        await catalog.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.False(catalog.IsCatalog); Assert.True(catalog.HasSingleCatalogDatabase);
        Assert.Equal("app", catalog.ActiveSession!.Database); Assert.Single(catalog.ActiveSession.Scope!.Databases);
        int requests = 0; catalog.WorkspaceRequested += (_, _) => requests++;
        await catalog.NewQueryCommand.ExecuteAsync();
        Assert.Equal(2, catalog.Sessions.Count); Assert.Equal(0, requests);
    });

    [Fact]
    public Task Catalog_MultipleDatabasesWaitForExplicitSelectionAndAllowSelectAll() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "reports"] };
        await using var catalog = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs());
        await catalog.InitializeAsync(); Assert.False(catalog.HasSingleCatalogDatabase); Assert.False(catalog.OpenSessionCommand.CanExecute(null));
        catalog.SelectAllDatabasesCommand.Execute(null); Assert.All(catalog.DatabaseChoices, item => Assert.True(item.IsSelected));
        Assert.True(catalog.IsAllDatabasesSelected); await catalog.ToggleAllDatabasesCommand.ExecuteAsync();
        Assert.All(catalog.DatabaseChoices, item => Assert.False(item.IsSelected)); Assert.False(catalog.IsAllDatabasesSelected);
        await catalog.ToggleAllDatabasesCommand.ExecuteAsync(); Assert.True(catalog.IsAllDatabasesSelected);
        WorkspacePageRequestEventArgs? requested = null; catalog.WorkspaceRequested += (_, request) => requested = request;
        await catalog.OpenSessionCommand.ExecuteAsync();
        var child = catalog; Assert.Null(requested);
        Assert.Equal(2, child.ActiveSession!.Scope!.Databases.Count); Assert.Empty(child.ActiveSession.Database); Assert.False(child.ActiveSession.CanRun);
        await child.ActiveSession.ExecuteAsync("SELECT 1"); Assert.Equal(0, server.Executions);
        await child.ActiveSession.ChangeDatabaseAsync("reports"); Assert.True(child.ActiveSession.CanRun);
        await child.ActiveSession.ChangeDatabaseAsync("outside"); Assert.Equal("reports", child.ActiveSession.Database);
        var previous = child.ActiveSession; previous.Sql = "SELECT draft";
        await child.NewQueryCommand.ExecuteAsync(); Assert.True(child.IsCatalog);
        Assert.Same(previous, child.ActiveSession); Assert.Single(child.Sessions);
        child.CancelDatabasePickerCommand.Execute(null); Assert.False(child.IsCatalog);
        Assert.Same(previous, child.ActiveSession); Assert.Equal("SELECT draft", previous.Sql);
    });

    [Fact]
    public Task Removal_CancelKeepsScopeAndLastDatabaseCannotBeRemoved() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestMySqlDialogs(); var server = new TestMySql();
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
        var config = new Configurations(); var dialogs = new TestMySqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, new TestMySql(), dialogs, "app", "reports", "audit"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app");
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app")); Assert.Equal(3, session.Scope!.Databases.Count);
        await page.RemoveSessionDatabaseAsync(new("another-instance", "app")); Assert.Equal(1, dialogs.RemovalConfirmations);
        dialogs.RemovalDecision = new("audit"); await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Equal("audit", session.Database); Assert.Equal(2, session.Scope.Databases.Count);
    });

    [Fact]
    public Task Ai_ReadsOnlySessionDatabasesAndRejectsSuggestionsAfterScopeChange() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestMySqlDialogs(); var server = new TestMySql();
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app"); server.MetadataDatabases.Clear();
        var context = await session.ReadAssistantContextAsync(true, false, TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "app", "reports" }, context.Databases); Assert.Empty(context.Results);
        Assert.All(server.MetadataDatabases.Where(name => name.Length > 0), name => Assert.Contains(name, context.Databases!));
        using var assistant = new MySqlAssistantViewModel(session, new ReplyClient()) { Prompt = "show tables" };
        await assistant.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(assistant.Messages.Last().SqlBlocks);
        dialogs.SelectedDatabases = [new(Configurations.Instance.Id, "audit")]; await page.AddSessionDatabaseCommand.ExecuteAsync();
        await assistant.ExecuteCommand.ExecuteAsync(suggestion); Assert.Equal(0, server.Executions);
        dialogs.ActionAllowed = false; await assistant.ClearCommand.ExecuteAsync(); Assert.Equal(2, assistant.Messages.Count);
        dialogs.ActionAllowed = true; await assistant.ClearCommand.ExecuteAsync(); Assert.Empty(assistant.Messages);
    });

    [Fact]
    public Task ObjectViewsShareOwnerAiAndClosingCleanSessionStillNeedsConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestMySqlDialogs { ActionAllowed = false };
        await using var page = Page(config, new TestMySql(), dialogs, "app"); await page.InitializeAsync();
        var session = page.ActiveSession!; var assistant = page.Assistant;
        await page.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app"));
        Assert.Same(session, page.ActiveSession); Assert.Same(assistant, page.Assistant); Assert.Equal(2, page.ActiveDocuments.Count);
        await page.CloseTabCommand.ExecuteAsync(session); Assert.Equal(2, page.Tabs.Count);
        dialogs.ActionAllowed = true; await page.CloseTabCommand.ExecuteAsync(session); Assert.Empty(page.Tabs); Assert.Empty(page.Sessions);
    });

    [Fact]
    public Task AiSuggestion_BecomesStaleAfterReconnectBeforeFirstExecution() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql(); var dialogs = new TestMySqlDialogs();
        await using var page = Page(config, server, dialogs, "app"); await page.InitializeAsync();
        var session = page.ActiveSession!;
        using var assistant = new MySqlAssistantViewModel(session, new ReplyClient()) { Prompt = "show users" };
        await assistant.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(assistant.Messages.Last().SqlBlocks);
        await page.ReconnectCommand.ExecuteAsync();
        await session.ExecuteAssistantSqlAsync(suggestion); Assert.True(session.HasError); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task RemovedDatabaseResultsRemainVisibleButCannotEnterAiContext() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql(); var dialogs = new TestMySqlDialogs { RemovalDecision = new() };
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
        var config = new Configurations(); var server = new TestMySql(); var dialogs = new TestMySqlDialogs { RemovalDecision = new() };
        await using var page = Page(config, server, dialogs, "app", "reports"); await page.InitializeAsync();
        var session = page.ActiveSession!; await session.ChangeDatabaseAsync("app");
        await page.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = page.ActiveTab!;
        var grid = table.SelectedResult!; var row = grid.Rows[0]; Assert.True(await table.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("keep this edit");
        await page.RemoveSessionDatabaseAsync(new(Configurations.Instance.Id, "app"));
        Assert.Equal(2, session.Scope!.Databases.Count); Assert.Equal("app", session.Database); Assert.Contains(table, page.Tabs);
        Assert.True(table.HasDataDraft); Assert.Equal("keep this edit", row.Cells[1].Value); Assert.Equal(0, server.Writes); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task Add_RejectsForeignInstanceAndPreservesExistingTarget() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var dialogs = new TestMySqlDialogs { SelectedDatabases = [new("other-instance", "outside")] };
        await using var page = Page(config, new TestMySql(), dialogs, "app"); await page.InitializeAsync();
        await page.AddSessionDatabaseCommand.ExecuteAsync(); Assert.True(page.HasError);
        Assert.Equal("app", page.ActiveSession!.Database); Assert.Single(page.ActiveSession.Scope!.Databases);
    });

    [Fact]
    public Task SelectAll_LoadsAllDatabasePagesBeforeSelecting() => fixture.RunOffscreenAsync(async () =>
    {
        var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "reports", "audit"], DatabasePageSize = 1 };
        await using var catalog = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs());
        await catalog.InitializeAsync(); Assert.Single(catalog.DatabaseChoices); Assert.False(catalog.HasSingleCatalogDatabase);
        Assert.False(catalog.DatabaseChoices[0].IsSelected);
        await catalog.SelectAllDatabasesCommand.ExecuteAsync(); Assert.Equal(3, catalog.DatabaseChoices.Count);
        Assert.All(catalog.DatabaseChoices, item => Assert.True(item.IsSelected)); Assert.True(catalog.OpenSessionCommand.CanExecute(null));
    });

    /// <summary>实际 WPF 窗体校验目录、范围条、对象树和 AI 的中英文布局。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, 1440)]
    [InlineData("zh-Hans", UiTheme.Light, 1440)]
    [InlineData("zh-Hans", UiTheme.Dark, 1120)]
    public Task Layout_CatalogAndSessionRespectScope(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(System.Globalization.CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var config = new Configurations(); var server = new TestMySql { DatabaseNames = ["app", "reports", "audit"] };
            await using var page = new MySqlWorkspaceViewModel(new(Configurations.Instance), config, server, config, new TestMySqlDialogs());
            await page.InitializeAsync();
            var view = new MySqlWorkspaceView { DataContext = page };
            var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout");
            var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); layout.Toolbar = null; header.DataContext = page; header.ShowWindowControls = true;
            var root = new DockPanel(); root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(view);
            Render("catalog"); Assert.True(page.IsCatalog); Assert.Empty(page.Sessions);
            if (width == 1440 && language == "zh-Hans")
            {
                var picker = Assert.Single(WindowTestVisuals.Descendants<DatabaseSessionPicker>(view));
                var surface = (Grid)picker.FindName("PickerSurface");
                Assert.Equal(new Point(64, 120), surface.TranslatePoint(new(), root));
                Assert.Equal(new Point(64, 270), ((Border)picker.FindName("PickerList")).TranslatePoint(new(), root));
                Assert.Equal(496, ((Border)picker.FindName("PickerList")).ActualHeight);
                Assert.Equal(new Point(64, 790), ((DockPanel)picker.FindName("PickerActions")).TranslatePoint(new(), root));
            }
            page.DatabaseChoices[0].IsSelected = true; page.DatabaseChoices[1].IsSelected = true;
            await page.OpenSessionCommand.ExecuteAsync(); Assert.True(page.OpenSessionCommand.LastException is null, page.OpenSessionCommand.LastException?.ToString()); Render("session");
            Assert.False(page.IsCatalog); Assert.Single(page.Sessions);
            Assert.Equal(2, ((WorkspaceTreeView)view.FindName("ObjectTree")).Items.Count);
            Assert.Single(WindowTestVisuals.Descendants<DatabaseScopePicker>(view));
            Assert.Equal(2, Assert.IsType<StackPanel>(header.ActionsContent).Children.Count);
            var script = page.ActiveSession!; script.Sql = "SELECT preserved;";
            await page.NewQueryCommand.ExecuteAsync(); Render("new-session");
            Assert.True(page.IsCatalog); page.CancelDatabasePickerCommand.Execute(null);
            Assert.Same(script, page.ActiveSession); Assert.Equal("SELECT preserved;", script.Sql);
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            void Render(string state)
            {
                root.Measure(new(width, 900)); root.Arrange(new(0, 0, width, 900)); root.UpdateLayout();
                string? output = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (string.IsNullOrEmpty(output)) return;
                Directory.CreateDirectory(output);
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)width, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(output, "MySql-" + state + "-" + language + "-" + theme + "-" + width + ".png")); encoder.Save(stream);
            }
        }
        finally { Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(null); }
    });

    private static MySqlWorkspaceViewModel Page(Configurations config, TestMySql server, TestMySqlDialogs dialogs, params string[] databases) =>
        new(new(Configurations.Instance), config, server, config, dialogs, initialDatabases: databases.Select(name => new DatabaseIdentity(Configurations.Instance.Id, name)).ToArray());
    private sealed class ReplyClient : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => Task.FromResult(AgentReply.FromContent("```sql\nSELECT 3;\n```"));
    }
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("engine", "Database", "Database", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
