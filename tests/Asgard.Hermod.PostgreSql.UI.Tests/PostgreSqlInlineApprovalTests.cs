using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>真实 Session 边界验证内联确认，不创建原生窗口或连接外部数据库。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PostgreSqlInlineApprovalTests(WpfTestFixture fixture)
{
    private const string Sql = "DELETE FROM users WHERE id = 1;";
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("insert")]
    [InlineData("stop")]
    public Task OneDecisionControlsOnlyTheDisplayedOperation(string decision) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
        await using var tab = new PostgreSqlTabViewModel("Session 1", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
        using var model = new PostgreSqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review a deletion" };
        await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
        var running = model.ExecuteCommand.ExecuteAsync(suggestion); var approval = Assert.IsType<AgentApprovalRequest>(model.PendingApproval);
        Assert.Equal(Sql, approval.Code); Assert.Contains("app", approval.Target); Assert.Contains("Session 1", approval.Target);
        Assert.True(model.IsBusy); Assert.False(model.SendCommand.CanExecute(null)); Assert.False(model.Archive.NewCommand.CanExecute(null)); Assert.Empty(server.ExecutedSql);
        Assert.Empty(model.Messages.Single(message => message.IsTool).Elapsed);
        model.Prompt = "next draft";
        if (decision == "approve") approval.ApproveCommand.Execute(null);
        else if (decision == "reject") approval.RejectCommand.Execute(null);
        else if (decision == "insert") approval.InsertCommand.Execute(null);
        else model.CancelCommand.Execute(null);
        await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Null(model.ExecuteCommand.LastException); Assert.False(model.IsBusy); Assert.False(approval.ApproveCommand.CanExecute(null)); Assert.Equal("next draft", model.Prompt);
        if (decision == "approve")
        {
            Assert.Equal((Sql, "app"), Assert.Single(server.ExecutedSql));
            Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("ChatActionCompleted"), model.Messages.Single(message => message.IsTool).Status);
        }
        else Assert.Empty(server.ExecutedSql);
        if (decision == "insert") Assert.Contains(Sql, tab.Sql);
        approval.ApproveCommand.Execute(null); Assert.Equal(decision == "approve" ? 1 : 0, server.ExecutedSql.Count);
        var history = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); model.Archive.ActiveThread = history;
        Assert.False(approval.ApproveCommand.CanExecute(null));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SourceChangeRevokesWaitingApproval(bool close) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = new PostgreSqlTabViewModel("Session 1", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
        using var model = new PostgreSqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
        var approval = model.PendingApproval!; if (close) model.Dispose(); else await tab.ChangeDatabaseAsync("reporting");
        await running.WaitAsync(TimeSpan.FromSeconds(3)); approval.ApproveCommand.Execute(null);
        Assert.False(approval.IsPending); Assert.Empty(server.ExecutedSql); Assert.False(model.IsBusy);
    });

    [Fact]
    public Task StopAfterDispatchRetainsUnknownOutcomeWithoutRetry() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var tab = new PostgreSqlTabViewModel("Session 1", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
        using var model = new PostgreSqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
        model.PendingApproval!.ApproveCommand.Execute(null);
        for (int attempt = 0; server.ExecutedSql.Count == 0 && attempt < 50; attempt++) await Task.Delay(10);
        Assert.Single(server.ExecutedSql); model.CancelCommand.Execute(null); await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(model.IsBusy); Assert.Single(server.ExecutedSql);
        Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("ChatActionUnknown"), model.Messages.Single(message => message.IsTool).Status);
        Assert.Empty(model.Messages.Single(message => message.IsTool).RecoveryPrompt);
    });

    [Fact]
    public Task UncooperativeProtocolDoesNotHoldAgentBusyOrRewriteUnknownHistory() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreQueryCancellation = true };
        await using var tab = new PostgreSqlTabViewModel("Session 1", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
        using var model = new PostgreSqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        try
        {
            await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
            model.PendingApproval!.ApproveCommand.Execute(null);
            for (int attempt = 0; server.ExecutedSql.Count == 0 && attempt < 50; attempt++) await Task.Delay(10);
            Assert.Single(server.ExecutedSql); model.CancelCommand.Execute(null); await running.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(model.IsBusy); Assert.True(tab.IsBusy); Assert.False(model.SendCommand.CanExecute(null));
            var record = model.Messages.Single(message => message.IsTool); string unknown = Asgard.Hermod.Abstractions.Localization.Text.Get("ChatActionUnknown");
            Assert.Equal(unknown, record.Status);
            server.BlockedQuery.TrySetResult();
            for (int attempt = 0; tab.IsBusy && attempt < 50; attempt++) await Task.Delay(10);
            Assert.False(tab.IsBusy); Assert.Single(server.ExecutedSql); Assert.Equal(unknown, record.Status);
        }
        finally { server.BlockedQuery.TrySetResult(); }
    });

    private sealed class Client : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentReply.FromContent("Review the complete statement before execution.\n```sql\n" + Sql + "\n```"));
    }
}
