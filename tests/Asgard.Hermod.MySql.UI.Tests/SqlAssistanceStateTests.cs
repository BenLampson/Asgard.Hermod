using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class SqlAssistanceStateTests(WpfTestFixture fixture)
{
    [Fact]
    public Task CompletionAndPlan_ReuseCurrentPhysicalSessionAndDoNotExecuteSqlWhileCompleting() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new());
        tab.Sql = "SELECT u.na FROM users u";
        var choices = await tab.CompleteAsync(new(tab.Sql, 11, 11, 0), TestContext.Current.CancellationToken);
        Assert.Contains(choices, item => item.Label == "name"); Assert.Equal(0, server.Executions);
        await tab.ExplainScopeAsync("SELECT id FROM users", 0, false);
        Assert.Equal(1, server.Opens); Assert.Equal("EXPLAIN SELECT id FROM users", Assert.Single(server.ExecutedSql).Sql);
        Assert.Null(tab.SelectedResult); Assert.NotEmpty(tab.PlanRawText); Assert.Equal("plan", tab.OutputView); Assert.Equal("explain", Assert.Single(tab.ExecutionTraces).Kind);
        Assert.Equal("SELECT id FROM users", Assert.Single(tab.ExecutionTraces).Sql);
    });

    [Fact]
    public Task Analyze_RequiresExplicitConfirmationAndNeverAnalyzesWriteStatements() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); await using var tab = Query(server, dialogs);
        await tab.ExplainScopeAsync("SELECT 1", 0, true);
        Assert.Equal(1, dialogs.AnalyzeConfirmations); Assert.Empty(server.ExecutedSql);
        dialogs.AnalyzeAllowed = true;
        await tab.ExplainScopeAsync("DELETE FROM users", 0, true);
        Assert.Equal(1, dialogs.AnalyzeConfirmations); Assert.Empty(server.ExecutedSql); Assert.True(tab.HasError);
        await tab.ExplainScopeAsync("SELECT 1", 0, true);
        Assert.Equal("EXPLAIN ANALYZE SELECT 1", Assert.Single(server.ExecutedSql).Sql);
        Assert.Equal("analyze", Assert.Single(tab.ExecutionTraces).Kind);
    });

    [Fact]
    public Task Plan_UsesCaretStatementAndRejectsMultiStatementSelection() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new());
        tab.Sql = "SELECT 'a;b'; SELECT 2;";
        await tab.ExplainScopeAsync("", tab.Sql.IndexOf("SELECT 2", StringComparison.Ordinal), false);
        Assert.Equal("EXPLAIN SELECT 2", Assert.Single(server.ExecutedSql).Sql);
        await tab.ExplainScopeAsync(tab.Sql, 0, false);
        Assert.Single(server.ExecutedSql); Assert.True(tab.HasError);
    });

    [Fact]
    public Task Trace_RecordsCancellationAndBoundsHistoryWithoutChangingDocument() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) }; await using var tab = Query(server, new());
        tab.Sql = "SELECT original";
        var pending = tab.ExecuteAsync("SELECT SLEEP(10)"); await Task.Yield(); tab.CancelCommand.Execute(null); await pending;
        Assert.False(Assert.Single(tab.ExecutionTraces).Succeeded);
        server.BlockedQuery = null;
        for (int i = 0; i < 51; i++) await tab.ExecuteAsync("SELECT " + i);
        Assert.Equal(50, tab.ExecutionTraces.Count); Assert.Equal("SELECT 50", tab.ExecutionTraces[0].Sql); Assert.Equal("SELECT original", tab.Sql);
    });

    [Fact]
    public Task Completion_FallsBackToKeywordsWhenMetadataIsDenied() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { ListFailure = new IOException("password=secret") }; await using var tab = Query(server, new());
        var suggestions = await tab.CompleteAsync(new("SEL", 3, 3, 0), TestContext.Current.CancellationToken);
        Assert.Contains(suggestions, item => item.InsertText == "SELECT"); Assert.Equal("", tab.Error); Assert.Empty(server.ExecutedSql);
    });

    [Theory]
    [InlineData("")]
    [InlineData("reporting")]
    public Task OpeningSession_UsesTabDatabaseWithoutMutatingSavedOptions(string database) => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var options = TestMySql.Connection;
        await using var tab = new MySqlTabViewModel("Query", database, MySqlObjectKind.Root, server, options, new TestMySqlDialogs());
        await tab.ExecuteAsync("SELECT 1");
        Assert.Equal(database, Assert.Single(server.OpenedOptions).Parameters["database"]);
        Assert.Equal("app", options.Parameters["database"]);
    });

    [Fact]
    public Task Plan_DoesNotReplaceQueryResultOrItsRequestTime() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql { Elapsed = TimeSpan.FromMilliseconds(12) }; await using var tab = Query(server, new());
        await tab.ExecuteAsync("SELECT 1"); var result = tab.SelectedResult; string summary = tab.QuerySummary;
        server.Elapsed = TimeSpan.FromMilliseconds(99); await tab.ExplainScopeAsync("SELECT 1", 0, false);
        Assert.Same(result, tab.SelectedResult); Assert.Equal(summary, tab.QuerySummary); Assert.Equal("plan", tab.OutputView);
        Assert.Equal(99, tab.ExecutionTraces[0].Elapsed.TotalMilliseconds);
    });

    private static MySqlTabViewModel Query(TestMySql server, TestMySqlDialogs dialogs) => new("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, dialogs);

    [Fact]
    public Task FailedPlan_KeepsPreviousSnapshotAndMarksItAsPreviousResult() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = Query(server, new());
        await tab.ExecuteAsync("SELECT 1"); var result = tab.SelectedResult;
        await tab.ExplainScopeAsync("SELECT 1", 0, false); var previous = tab.Plan; string raw = tab.PlanRawText;
        server.ExecuteError = new IOException("password=private-plan-test");
        await tab.ExplainScopeAsync("SELECT 2", 0, false);
        Assert.Same(previous, tab.Plan); Assert.True(previous!.IsStale); Assert.Equal(raw, tab.PlanRawText);
        Assert.Same(result, tab.SelectedResult); Assert.Equal("messages", tab.OutputView); Assert.DoesNotContain("private-plan-test", tab.Error);
        Assert.False(tab.ExecutionTraces[0].Succeeded); Assert.Equal("SELECT 2", tab.ExecutionTraces[0].Sql);
    });
}
