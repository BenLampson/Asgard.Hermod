using Asgard.Hermod.Agent;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>手动和自动模式均保留同一流式消息，完整回复之前不发布 SQL 执行建议。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MySqlStreamingAssistantTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("manual")]
    [InlineData("automatic")]
    public Task PartialReplyKeepsThinkingSeparateAndWaitsBeforePublishingSql(string mode) => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql();
        await using var tab = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        var client = new StreamingClient(); using var assistant = new MySqlAssistantViewModel(tab, client) { ExecutionMode = mode, Prompt = "question" };
        var pending = assistant.SendCommand.ExecuteAsync(); await client.Entered.Task;
        var message = assistant.Messages[^1]; Assert.True(message.IsStreaming); Assert.Empty(message.Content); Assert.Empty(message.SqlBlocks);
        Assert.Equal("thinking only", message.Thinking.Content); message.Thinking.IsExpanded = true;
        assistant.Prompt = "next draft";
        await client.Receive!(new("```sql\nSELECT 1;\n```", "thinking only", false, TimeSpan.FromSeconds(1)), TestContext.Current.CancellationToken);
        Assert.Empty(message.SqlBlocks); Assert.Empty(server.ExecutedSql);
        client.Done.SetResult(AgentReply.FromContent(message.Content) with { Thinking = "thinking only", ThinkingElapsed = TimeSpan.FromSeconds(1) }); await pending;
        Assert.Null(assistant.SendCommand.LastException); Assert.Empty(assistant.Error); Assert.Same(message, assistant.Messages[^1]);
        Assert.True(message.Thinking.IsExpanded); Assert.False(message.IsStreaming); Assert.Equal("SELECT 1;", Assert.Single(message.SqlBlocks).Sql);
        Assert.Equal("next draft", assistant.Prompt); Assert.Empty(server.ExecutedSql);
    });
    private sealed class StreamingClient : IAgentClient
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<AgentReply> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Func<AgentStreamUpdate, CancellationToken, ValueTask>? Receive { get; private set; }
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<AgentReply> CompleteStreamingAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools, Func<AgentStreamUpdate, CancellationToken, ValueTask>? receive, CancellationToken cancellationToken = default)
        {
            Receive = receive; await receive!(new("", "thinking only", true, TimeSpan.Zero), cancellationToken); Entered.TrySetResult();
            return await Done.Task.WaitAsync(cancellationToken);
        }
    }
}
