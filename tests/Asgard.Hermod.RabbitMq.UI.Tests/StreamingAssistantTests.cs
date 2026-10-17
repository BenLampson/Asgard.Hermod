using Asgard.Hermod.Agent;

namespace Asgard.Hermod.RabbitMq.UI.Tests;

/// <summary>快照助手逐步展示模型输出，完成后保留正在编辑的新草稿。</summary>
[Collection(HeadlessCollection.Name)]
public sealed class StreamingAssistantTests(HeadlessFixture fixture)
{
    [Fact]
    public Task StreamingReplyRetainsThinkingAndDoesNotEraseNextDraft() => fixture.RunAsync(async () =>
    {
        var client = new StreamingClient(); var model = new RabbitMqAssistantViewModel(client) { Prompt = "question" }; model.Attach("snapshot");
        var pending = model.SendCommand.ExecuteAsync(); Assert.Equal(2, model.Messages.Count); var reply = model.Messages[^1];
        Assert.True(reply.IsStreaming); Assert.Empty(reply.Content); Assert.Equal("thinking", reply.Thinking.Content);
        model.Prompt = "next draft"; reply.Thinking.IsExpanded = true;
        client.Done.SetResult(AgentReply.FromContent("answer") with { Thinking = "thinking", ModelIdentity = "actual" }); await pending;
        Assert.Same(reply, model.Messages[^1]); Assert.Equal("answer", reply.Content); Assert.False(reply.IsStreaming); Assert.True(reply.Thinking.IsExpanded);
        Assert.Equal("next draft", model.Prompt); Assert.Equal("actual", reply.ModelIdentity); Assert.Empty(model.Error);
    });
    private sealed class StreamingClient : IAgentClient
    {
        internal TaskCompletionSource<AgentReply> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async Task<AgentReply> CompleteStreamingAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools, Func<AgentStreamUpdate, CancellationToken, ValueTask>? receive, CancellationToken cancellationToken = default)
        { await receive!(new("", "thinking", true, TimeSpan.Zero), cancellationToken); return await Done.Task.WaitAsync(cancellationToken); }
    }
}
