using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>验证 PostgreSQL 的数据库连接、模式身份和查询生命周期，避免沿用 MySQL 隐式跨库行为。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PostgreSqlWorkspaceTests(WpfTestFixture fixture)
{
    [Fact]
    public Task ObjectTabs_DistinguishSameNamesInDifferentSchemas() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var configs = new Configurations();
        await using var page = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, new TestPostgreSqlDialogs(), "app");
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        await page.OpenObjectAsync(new(PostgreSqlObjectKind.Table, "users", "app", Schema: "public"));
        var first = page.ActiveTab;
        await page.OpenObjectAsync(new(PostgreSqlObjectKind.Table, "users", "app", Schema: "sales"));
        Assert.NotSame(first, page.ActiveTab); Assert.Equal("sales", page.ActiveTab!.Schema);
        Assert.Contains(("app", "sales", "users"), server.SchemaReads);
        await page.OpenObjectAsync(new(PostgreSqlObjectKind.Table, "users", "app", Schema: "public"));
        Assert.Same(first, page.ActiveTab); Assert.Equal(3, page.Tabs.Count);
    });

    [Fact]
    public Task DatabaseWindow_UsesTargetConnectionAndSchemaRoot() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var configs = new Configurations();
        await using var page = new PostgreSqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, new TestPostgreSqlDialogs(), focusedDatabase: "reporting");
        await page.InitializeAsync(TestContext.Current.CancellationToken);
        Assert.StartsWith("postgresql.session:", page.WorkspaceContextId);
        Assert.Equal("reporting", server.OpenedOptions[0].Parameters["database"]);
        Assert.Equal("reporting", page.ActiveTab!.Database);
        Assert.All(page.NavigationRoot!.Children, node => Assert.Equal(PostgreSqlObjectKind.Schema, node.Object.Kind));
        await page.ActiveTab.ExecuteAsync("SELECT current_database()");
        Assert.All(server.OpenedOptions, options => Assert.Equal("reporting", options.Parameters["database"]));
    });

    [Fact]
    public Task QueryDatabaseChange_DisposesOldSessionAndPreservesDocument() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = Query(server);
        tab.Sql = "SELECT 1;"; await tab.ExecuteAsync(tab.Sql);
        server.InTransaction = true;
        await tab.ChangeDatabaseAsync("reporting");
        Assert.Equal(1, server.Rollbacks); Assert.Equal(1, server.Disposals);
        Assert.Equal("SELECT 1;", tab.Sql); Assert.Equal(1, server.Executions);
        await tab.ExecuteAsync("SELECT 2;");
        Assert.Equal("reporting", server.OpenedOptions.Last().Parameters["database"]);
        Assert.Equal("reporting", server.ExecutedSql.Last().Database);
        await tab.ChangeDatabaseAsync(""); Assert.Equal("reporting", tab.Database); Assert.True(tab.HasError);
    });

    [Fact]
    public Task QueryScopes_RespectDollarQuotedStatementsAndExplainNeverExecutesSql() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = Query(server);
        tab.Sql = "SELECT $$semi;colon$$; SELECT 2;";
        await tab.ExecuteScopeAsync("", 12, false);
        Assert.Equal("SELECT $$semi;colon$$", server.ExecutedSql.Last().Sql);
        await tab.ExecuteScopeAsync("SELECT 7", 0, false); Assert.Equal("SELECT 7", server.ExecutedSql.Last().Sql);
        await tab.ExecuteScopeAsync("ignored", 0, true); Assert.Equal(tab.Sql, server.ExecutedSql.Last().Sql);
        await tab.ExplainScopeAsync("SELECT 9", 0);
        Assert.Equal(3, server.Executions); Assert.Equal("SELECT 9", Assert.Single(server.ExplainedSql));
        Assert.False(tab.SelectedResult!.IsEditable); Assert.Equal(4, tab.ExecutionTraces.Count);
        Assert.Equal("explain", tab.SelectedTrace!.Kind); Assert.Equal("SELECT 9", tab.SelectedTrace.Sql);
    });

    [Fact]
    public Task Completion_QuotesSchemaTablesAndReadsExplicitTableColumns() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = Query(server);
        var tables = await tab.CompleteAsync(new("SELECT * FROM sales.us", 22, 22, 0), TestContext.Current.CancellationToken);
        Assert.Contains(tables, item => item.InsertText == "\"users\"");
        var columns = await tab.CompleteAsync(new("SELECT users.na", 15, 15, 0), TestContext.Current.CancellationToken);
        Assert.Contains(columns, item => item.InsertText == "\"name\"");
        Assert.Empty(server.ExecutedSql);
    });

    [Fact]
    public Task AssistantContext_UsesPostgreSqlDialectAndNeverIncludesResultsByDefault() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = Query(server);
        tab.Sql = "SELECT * FROM sales.users"; await tab.ExecuteAsync(tab.Sql);
        var context = await tab.ReadAssistantContextAsync(true, false, TestContext.Current.CancellationToken);
        Assert.Equal("PostgreSQL", context.Dialect); Assert.Equal("app", context.Database);
        Assert.Contains("\"sales\".\"users\"", context.Schema); Assert.Empty(context.Results);
        var client = new ReplyClient(); using var assistant = new PostgreSqlAssistantViewModel(tab, client) { Prompt = "show users" };
        await assistant.SendCommand.ExecuteAsync();
        Assert.Equal(1, server.Executions); Assert.Equal(2, assistant.Messages.Count);
        assistant.InsertCommand.Execute(Assert.Single(assistant.Messages.Last().SqlBlocks)); Assert.Contains("SELECT 3;", tab.Sql); Assert.Equal(1, server.Executions);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnknownWrite_IsNotReplayedAndVerificationPreservesDraft(bool committed) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { Unknown = true, CommitBeforeUnknown = committed };
        await using var tab = new PostgreSqlTabViewModel("users", "app", PostgreSqlObjectKind.Table, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
        await tab.ReadAsync(); var grid = tab.SelectedResult!; var row = grid.Rows[0];
        Assert.True(await tab.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("draft");
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.True(tab.OutcomeUnknown);
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.Equal(1, server.Writes);
        await tab.VerifyCommand.ExecuteAsync(); Assert.False(tab.OutcomeUnknown); Assert.Equal(!committed, tab.HasDataDraft); Assert.Equal(1, server.Writes);
    });

    [Fact]
    public Task CancelledQuery_DoesNotReenterAndCanBeExplicitlyRunAgain() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) }; await using var tab = Query(server);
        var pending = tab.ExecuteAsync("SELECT pg_sleep(10)"); await Task.Yield(); Assert.True(tab.IsBusy);
        await tab.ExecuteAsync("SELECT 2"); Assert.Equal(1, server.Executions);
        tab.CancelCommand.Execute(null); await pending; Assert.False(tab.IsBusy);
        server.BlockedQuery = null; await tab.ExecuteAsync("SELECT 3"); Assert.Equal(2, server.Executions);
    });

    [Fact]
    public Task ConcurrentCompletionAndExecution_ShareExactlyOnePhysicalSession() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { BlockedOpen = new(TaskCreationOptions.RunContinuationsAsynchronously) }; await using var tab = Query(server);
        var completion = tab.CompleteAsync(new("SEL", 3, 3, 0), TestContext.Current.CancellationToken);
        await Task.Yield(); Assert.Equal(1, server.Opens);
        var execution = tab.ExecuteAsync("SELECT 1"); await Task.Yield(); Assert.Equal(1, server.Opens);
        server.BlockedOpen.SetResult(); await Task.WhenAll(completion, execution);
        Assert.Equal(1, server.Opens); Assert.Equal(1, server.Executions); Assert.Single(server.Sessions);
    });

    [Fact]
    public Task ClosingDuringCompletion_DisposesLateConnection() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { BlockedOpen = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var tab = Query(server);
        var completion = tab.CompleteAsync(new("SEL", 3, 3, 0), TestContext.Current.CancellationToken); await Task.Yield();
        var closing = tab.DisposeAsync().AsTask(); server.BlockedOpen.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => completion); await closing;
        Assert.Equal(1, server.Disposals); Assert.Equal(0, server.Executions);
    });

    [Fact]
    public Task AssistantSuggestion_IsRejectedIfDatabaseChangesDuringConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
        await using var tab = new PostgreSqlTabViewModel("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
        using var assistant = new PostgreSqlAssistantViewModel(tab, new ReplyClient()) { Prompt = "show users" };
        await assistant.SendCommand.ExecuteAsync();
        var suggestion = Assert.Single(assistant.Messages.Last().SqlBlocks);
        dialogs.AssistantConfirmation = async () => { await tab.ChangeDatabaseAsync("reporting"); return true; };
        await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Equal("reporting", tab.Database); Assert.Equal(1, dialogs.AssistantConfirmations); Assert.Equal(0, server.Executions);
        await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Equal(1, dialogs.AssistantConfirmations); Assert.Equal(0, server.Executions); Assert.True(tab.HasError);
    });

    private static PostgreSqlTabViewModel Query(TestPostgreSql server) => new("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
    private sealed class ReplyClient : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => Task.FromResult(AgentReply.FromContent("```sql\nSELECT 3;\n```"));
    }
    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("postgresql", "PostgreSQL", "PostgreSQL", "database", [], TypeId: PostgreSqlIds.Type, ProviderId: PostgreSqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestPostgreSql.Connection);
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
