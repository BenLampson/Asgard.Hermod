using Asgard.Hermod.Agent;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>真实 Session 边界验证内联确认，不创建原生窗口或连接外部数据库。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MySqlInlineApprovalTests(WpfTestFixture fixture)
{
    private const string Sql = "DELETE FROM users WHERE id = 1;";
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("insert")]
    [InlineData("stop")]
    public Task OneDecisionControlsOnlyTheDisplayedOperation(string decision) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs();
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, dialogs);
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review a deletion" };
        await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
        var running = model.ExecuteCommand.ExecuteAsync(suggestion); var approval = Assert.IsType<AgentApprovalRequest>(model.PendingApproval);
        Assert.Equal(Sql, approval.Code); Assert.Contains("app", approval.Target); Assert.Contains("Session 1", approval.Target);
        Assert.True(model.IsBusy); Assert.False(model.SendCommand.CanExecute(null)); Assert.False(model.Archive.NewCommand.CanExecute(null)); Assert.Empty(server.ExecutedSql);
        Assert.Empty(suggestion.Execution.Elapsed); Assert.Equal(2, model.Messages.Count);
        Assert.Same(approval, suggestion.Execution.Approval); Assert.DoesNotContain(model.Messages, message => message.IsTool);
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
            Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("ChatSqlExecuted"), suggestion.Execution.Status);
        }
        else Assert.Empty(server.ExecutedSql);
        if (decision == "insert") Assert.Contains(Sql, tab.Sql);
        approval.ApproveCommand.Execute(null); Assert.Equal(decision == "approve" ? 1 : 0, server.ExecutedSql.Count);
        var history = model.Archive.ActiveThread; model.Archive.NewCommand.Execute(null); model.Archive.ActiveThread = history;
        Assert.False(approval.ApproveCommand.CanExecute(null));
        Assert.Same(suggestion, Assert.Single(model.Messages[^1].SqlBlocks)); Assert.Equal(decision == "approve", suggestion.Execution.HasExecuted);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SourceChangeRevokesWaitingApproval(bool close) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
        var approval = model.PendingApproval!; if (close) model.Dispose(); else await tab.ChangeDatabaseAsync("reporting");
        await running.WaitAsync(TimeSpan.FromSeconds(3)); approval.ApproveCommand.Execute(null);
        Assert.False(approval.IsPending); Assert.Empty(server.ExecutedSql); Assert.False(model.IsBusy);
    });

    [Fact]
    public Task StopAfterDispatchRetainsUnknownOutcomeWithoutRetry() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
        model.PendingApproval!.ApproveCommand.Execute(null);
        for (int attempt = 0; server.ExecutedSql.Count == 0 && attempt < 50; attempt++) await Task.Delay(10);
        Assert.Single(server.ExecutedSql); model.CancelCommand.Execute(null); await running.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.False(model.IsBusy); Assert.Single(server.ExecutedSql);
        Assert.Equal(Asgard.Hermod.Abstractions.Localization.Text.Get("ChatActionUnknown"), Assert.Single(model.Messages[^1].SqlBlocks).Execution.Status);
        Assert.False(Assert.Single(model.Messages[^1].SqlBlocks).Execution.CanExecute);
        Assert.False(model.ExecuteCommand.CanExecute(Assert.Single(model.Messages[^1].SqlBlocks)));
    });

    [Fact]
    public Task UncooperativeProtocolDoesNotHoldAgentBusyOrRewriteUnknownHistory() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously), IgnoreQueryCancellation = true };
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        try
        {
            await model.SendCommand.ExecuteAsync(); var running = model.ExecuteCommand.ExecuteAsync(Assert.Single(model.Messages[^1].SqlBlocks));
            model.PendingApproval!.ApproveCommand.Execute(null);
            for (int attempt = 0; server.ExecutedSql.Count == 0 && attempt < 50; attempt++) await Task.Delay(10);
            Assert.Single(server.ExecutedSql); model.CancelCommand.Execute(null); await running.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(model.IsBusy); Assert.True(tab.IsBusy); Assert.False(model.SendCommand.CanExecute(null));
            var record = Assert.Single(model.Messages[^1].SqlBlocks).Execution; string unknown = Asgard.Hermod.Abstractions.Localization.Text.Get("ChatActionUnknown");
            Assert.Equal(unknown, record.Status);
            server.BlockedQuery.TrySetResult();
            for (int attempt = 0; tab.IsBusy && attempt < 50; attempt++) await Task.Delay(10);
            Assert.False(tab.IsBusy); Assert.Single(server.ExecutedSql); Assert.Equal(unknown, record.Status);
        }
        finally { server.BlockedQuery.TrySetResult(); }
    });

    /// <summary>再次执行必须建立新审批，旧审批、双击和历史切换都不能重复派发。</summary>
    [Fact]
    public Task ReexecutionUsesTheSameSuggestionAndRequiresFreshApproval() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql();
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
        var first = model.ExecuteCommand.ExecuteAsync(suggestion); var oldApproval = model.PendingApproval!;
        await model.ExecuteCommand.ExecuteAsync(suggestion); Assert.Same(oldApproval, model.PendingApproval);
        oldApproval.ApproveCommand.Execute(null); await first;
        Assert.True(suggestion.Execution.HasExecuted); Assert.Contains("Alice", suggestion.Execution.Output);
        string output = suggestion.Execution.Output;
        tab.Sql = "SELECT other"; await tab.ExecuteAsync(tab.Sql); Assert.Equal(output, suggestion.Execution.Output);
        int before = server.ExecutedSql.Count;
        var second = model.ExecuteCommand.ExecuteAsync(suggestion); var newApproval = model.PendingApproval!;
        Assert.NotSame(oldApproval, newApproval); Assert.Equal(before, server.ExecutedSql.Count);
        oldApproval.ApproveCommand.Execute(null); Assert.Equal(before, server.ExecutedSql.Count);
        newApproval.ApproveCommand.Execute(null); await second;
        Assert.Equal(before + 1, server.ExecutedSql.Count); Assert.Equal(2, model.Messages.Count);
        Assert.True(suggestion.Execution.HasExecuted); Assert.Same(suggestion, Assert.Single(model.Messages[^1].SqlBlocks));
    });

    /// <summary>目标失效时安全错误留在原卡片，不新增消息或派发旧 SQL。</summary>
    [Fact]
    public Task StaleSuggestionReportsFailureInPlace() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql();
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
        await tab.ChangeDatabaseAsync("reporting"); await model.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Empty(server.ExecutedSql); Assert.Equal(2, model.Messages.Count); Assert.False(suggestion.Execution.HasExecuted);
        Assert.Contains(Asgard.Hermod.Abstractions.Localization.Text.Get("MySqlAssistantContextChanged"), suggestion.Execution.Result);
    });

    /// <summary>执行错误留在原建议中，不能误标成功或混入之前的结果。</summary>
    [Fact]
    public Task FailedExecutionKeepsSafeErrorAndNoPreviousPreview() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql();
        await using var tab = new MySqlTabViewModel("Session 1", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        using var model = new MySqlAssistantViewModel(tab, new Client(), useInlineApproval: true) { Prompt = "Review deletion" };
        await model.SendCommand.ExecuteAsync(); var suggestion = Assert.Single(model.Messages[^1].SqlBlocks);
        await tab.ExecuteAsync("SELECT previous");
        server.ExecutionResult = _ => new([], [new(1, 0, false)], new MySqlFailure("MySqlCommandFailed", "TEST_FAILURE"), TimeSpan.FromMilliseconds(2), new(true, false));
        var running = model.ExecuteCommand.ExecuteAsync(suggestion); model.PendingApproval!.ApproveCommand.Execute(null); await running;
        Assert.Equal(Asgard.Hermod.UI.Models.AgentSqlExecutionState.Failed, suggestion.Execution.State);
        Assert.Contains(Asgard.Hermod.Abstractions.Localization.Text.Get("MySqlCommandFailed"), suggestion.Execution.Result);
        Assert.Empty(suggestion.Execution.Output); Assert.True(suggestion.Execution.HasExecuted); Assert.Equal(2, model.Messages.Count);
    });

    private sealed class Client : IAgentClient
    {
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
            => Task.FromResult(AgentReply.FromContent("Review the complete statement before execution.\n```sql\n" + Sql + "\n```"));
    }
}
