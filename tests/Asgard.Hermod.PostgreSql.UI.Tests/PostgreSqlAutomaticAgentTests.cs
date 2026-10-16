using System.Text.Json;
using Asgard.Hermod.Agent;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>验证真实页签、自动查询闭环和手动写入确认，所有数据库及模型均为进程内替身。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class PostgreSqlAutomaticAgentTests(WpfTestFixture fixture)
{
    /// <summary>自动查询使用独立会话并反馈结果，不改写编辑器或用户事务。</summary>
    [Fact]
    public Task AutomaticQueryFeedsResultAndPreservesEditorAndTransaction() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { InTransaction = true };
        await using var tab = Tab(server, new());
        tab.Sql = "SELECT original_draft";
        var client = new Client([Query(), AgentReply.FromContent("Analyzed Alice")]);
        using var assistant = new PostgreSqlAssistantViewModel(tab, client) { ExecutionMode = "automatic", Prompt = "analyze" };
        await assistant.SendCommand.ExecuteAsync();
        Assert.Null(assistant.SendCommand.LastException); Assert.Empty(assistant.Error);
        Assert.Single(server.ReadOnlyQueries); Assert.Equal(2, server.Opens);
        Assert.Contains("Alice", client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
        Assert.Equal("SELECT original_draft", tab.Sql); Assert.True(server.InTransaction); Assert.Equal(0, server.Rollbacks);
        Assert.Null(Assert.Single(tab.Results).Result.EditableTable);
        Assert.Single(assistant.Messages, message => message.IsTool);
        Assert.Equal("Analyzed Alice", assistant.Messages[^1].Content); Assert.Empty(assistant.Prompt);
        Assert.Equal("agent-query", Assert.Single(tab.ExecutionTraces).Kind);
    });

    /// <summary>自动模式下的修改建议仍需用户点击并确认，拒绝后不能进入协议执行。</summary>
    [Fact]
    public Task ModificationSuggestionStillRequiresOriginalConfirmation() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
        await using var tab = Tab(server, dialogs);
        using var assistant = new PostgreSqlAssistantViewModel(tab, new Client([AgentReply.FromContent("Review this change.\n```sql\nUPDATE users SET name='changed';\n```")])) { IsAutomaticExecution = true, Prompt = "change" };
        await assistant.SendCommand.ExecuteAsync();
        Assert.Empty(server.ReadOnlyQueries); Assert.Empty(server.ExecutedSql);
        var suggestion = Assert.Single(assistant.Messages[^1].SqlBlocks);
        await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Equal(1, dialogs.AssistantConfirmations); Assert.Empty(server.ExecutedSql);
        dialogs.AssistantConfirmation = () => Task.FromResult(true);
        await assistant.ExecuteCommand.ExecuteAsync(suggestion);
        Assert.Single(server.ExecutedSql); Assert.Empty(server.ReadOnlyQueries);
    });

    /// <summary>取消立即传递给当前查询，进行中的模式不能被切换，不继续请求下一步分析。</summary>
    [Fact]
    public Task CancelStopsQueryAndKeepsDraft() => fixture.RunOffscreenAsync(async () =>
    {
        var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = new TestPostgreSql { BlockedQuery = blocked };
        await using var tab = Tab(server, new());
        var client = new Client([Query()]);
        using var assistant = new PostgreSqlAssistantViewModel(tab, client) { IsAutomaticExecution = true, Prompt = "keep draft" };
        var sending = assistant.SendCommand.ExecuteAsync();
        Assert.True(assistant.IsBusy); Assert.Single(server.ReadOnlyQueries);
        assistant.ExecutionMode = "manual"; Assert.True(assistant.IsAutomaticExecution); Assert.False(assistant.CanChangeExecutionMode);
        assistant.CancelCommand.Execute(null); await sending;
        Assert.Single(client.Requests); Assert.Empty(assistant.Prompt); assistant.RestoreDraftCommand.Execute(null); Assert.Equal("keep draft", assistant.Prompt); Assert.False(assistant.IsBusy);
    });

    /// <summary>数据库在模型响应期间变化会废弃旧任务，旧查询不会打开自动执行连接。</summary>
    [Fact]
    public Task TargetChangeBeforeToolReplyRejectsStaleQuery() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); await using var tab = Tab(server, new());
        var client = new Client([Query()]) { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        using var assistant = new PostgreSqlAssistantViewModel(tab, client) { IsAutomaticExecution = true, Prompt = "analyze" };
        var sending = assistant.SendCommand.ExecuteAsync();
        await tab.ChangeDatabaseAsync("other");
        client.Pending.SetResult(); await sending;
        Assert.Empty(server.ReadOnlyQueries); Assert.NotEmpty(assistant.Error);
    });

    /// <summary>未知查询失败停止后续工具并隐藏 SDK、连接串和凭据原文。</summary>
    [Fact]
    public Task UnknownQueryFailureIsSafeAndNeverRetried() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql { ReadOnlyError = new IOException("credential-and-sdk-secret") };
        await using var tab = Tab(server, new());
        var client = new Client([Query(), AgentReply.FromContent("Check the connection")]);
        using var assistant = new PostgreSqlAssistantViewModel(tab, client) { IsAutomaticExecution = true, Prompt = "analyze" };
        await assistant.SendCommand.ExecuteAsync();
        Assert.Single(server.ReadOnlyQueries); Assert.Empty(client.Tools[1]);
        Assert.DoesNotContain("credential-and-sdk-secret", string.Join("", assistant.Messages.Select(message => message.Content)));
        Assert.Contains("unknown", client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
    });

    /// <summary>真实模式选择器以稳定 ID 双向绑定，工具结果使用共享消息而非再次执行的建议。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ModeSelectorDrivesAutomaticExecution(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(System.Globalization.CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            var server = new TestPostgreSql(); await using var tab = Tab(server, new());
            using var assistant = new PostgreSqlAssistantViewModel(tab, new Client([Query(), AgentReply.FromContent("Analyzed")])) { Prompt = "analyze" };
            var view = new PostgreSqlAssistantView { DataContext = assistant };
            var root = new Border { Child = view }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            void Layout() { root.Measure(new Size(320, 760)); root.Arrange(new Rect(0, 0, 320, 760)); root.UpdateLayout(); }
            using var trace = new WpfBindingTrace();
            Layout(); await WpfTestFixture.DrainDispatcherAsync(); Layout();
            var panel = Assert.IsType<AgentPanel>(view.FindName("ChatPanel"));
            var selector = Assert.IsType<SegmentedSelector>(panel.ComposerOptions);
            Assert.Equal("manual", selector.SelectedValue);
            selector.SelectedValue = "automatic";
            selector.GetBindingExpression(System.Windows.Controls.Primitives.Selector.SelectedValueProperty)!.UpdateSource();
            Assert.True(assistant.IsAutomaticExecution);
            await assistant.SendCommand.ExecuteAsync();
            await WpfTestFixture.DrainDispatcherAsync(); Layout();
            Assert.Single(server.ReadOnlyQueries);
            Assert.Contains(WindowTestVisuals.Descendants<AgentMessageView>(root), message => message.IsTool);
            Assert.Empty(trace.Messages);
        }
        finally { themes.SetTheme(UiTheme.Light); Asgard.Hermod.Abstractions.Localization.Text.SetApplicationCulture(null); }
    });

    private static PostgreSqlTabViewModel Tab(TestPostgreSql server, TestPostgreSqlDialogs dialogs) => new("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
    private static AgentReply Query() => new("", []) { ToolCalls = [new("query", "sql_query", JsonSerializer.Serialize(new { database = "app", sql = "SELECT id,name FROM users LIMIT 20" }))] };

    /// <summary>记录工具与反馈，不访问外部模型。</summary>
    private sealed class Client(IReadOnlyList<AgentReply> replies) : IAgentClient
    {
        internal List<AgentMessage[]> Requests { get; } = [];
        internal List<AgentToolDefinition[]> Tools { get; } = [];
        internal TaskCompletionSource? Pending { get; init; }
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => CompleteWithToolsAsync(messages, [], cancellationToken);
        public async Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        {
            Requests.Add(messages.ToArray()); Tools.Add(tools.ToArray());
            if (Pending is not null) await Pending.Task.WaitAsync(cancellationToken);
            return replies[Requests.Count - 1];
        }
    }
}
