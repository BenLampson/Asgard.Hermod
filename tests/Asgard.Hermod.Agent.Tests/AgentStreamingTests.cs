using System.Text;
using System.Text.Json;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>真实流边界验收：首包、分包、断流、取消与工具完整性均不访问网络。</summary>
public sealed class AgentStreamingTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    internal static string Event(string delta, string? finish = null) => "data: " + JsonSerializer.Serialize(new { model = "actual-model", choices = new[] { new { index = 0, delta = JsonSerializer.Deserialize<JsonElement>(delta), finish_reason = finish } } }) + "\n\n";
    internal static OpenAiCompatibleAgentClient Create(HttpClient http, TimeProvider? clock = null)
    {
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "secret");
        return new(http, configuration) { Clock = clock ?? TimeProvider.System };
    }
    private static async Task Wait(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(5), Token);

    [Fact]
    public async Task ThinkingAndBodyArriveBeforeCompletionAndNeverMix()
    {
        using var stream = new ControlledSseStream(); using var handler = new StreamingHttpHandler(stream); using var http = new HttpClient(handler);
        var updates = new List<AgentStreamUpdate>(); var thinking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var body = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = Create(http).CompleteStreamingAsync([new(AgentRole.User, "question")], null, (update, _) =>
        {
            updates.Add(update); if (update.IsThinking) thinking.TrySetResult(); if (update.Content.Length > 0) body.TrySetResult(); return ValueTask.CompletedTask;
        }, Token);
        stream.Push(Event("""{"reasoning_content":"Compare the supplied evidence."}""")); await Wait(thinking.Task);
        Assert.False(pending.IsCompleted); Assert.Empty(updates[^1].Content); Assert.True(updates[^1].IsThinking);
        stream.Push(Event("""{"content":"Observed answer."}""")); await Wait(body.Task);
        Assert.False(pending.IsCompleted); Assert.False(updates[^1].IsThinking); Assert.Equal("Compare the supplied evidence.", updates[^1].Thinking);
        stream.Push(Event("{}", "stop") + "data: [DONE]\n\n");
        var reply = await pending.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.Equal("Observed answer.", reply.Content); Assert.Equal("Compare the supplied evidence.", reply.Thinking); Assert.Equal("actual-model", reply.ModelIdentity);
        Assert.True(stream.WasDisposed); Assert.Equal("text/event-stream", handler.Accept);
        using var request = JsonDocument.Parse(handler.Body); Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
    }

    [Theory]
    [InlineData("\n", "reasoning_content")]
    [InlineData("\r\n", "reasoning")]
    [InlineData("\r", "thinking")]
    public async Task Utf8FragmentsBomHeartbeatAndMultilineDataAreParsed(string newline, string field)
    {
        using var stream = new ControlledSseStream(); using var handler = new StreamingHttpHandler(stream); using var http = new HttpClient(handler);
        // JSON 格式允许字段之间换行；SSE 多行 data 必须用换行连接。
        string reasoning = "data: {\"choices\":[{\"index\":0,\n" + $"data: \"delta\":{{\"{field}\":\"思考🙂\"}},\"finish_reason\":null}}]}}\n\n";
        stream.Push(("\uFEFF: heartbeat\n\nevent: message\nid: 42\n" + reasoning + Event("""{"content":"正文🙂"}""", "stop") + "data: [DONE]\n\n").Replace("\n", newline), splitBytes: true);
        var reply = await Create(http).CompleteAsync([new(AgentRole.User, "question")], Token);
        Assert.Equal("思考🙂", reply.Thinking); Assert.Equal("正文🙂", reply.Content);
    }

    [Theory]
    [InlineData("data: {\"error\":{\"message\":\"secret\"}}\n\n")]
    [InlineData("data: invalid secret json\n\n")]
    [InlineData("data: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[null]}\n\ndata: [DONE]\n\n")]
    [InlineData("data: {\"choices\":[{\"index\":1,\"delta\":{\"content\":\"secret\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n")]
    public async Task MalformedStreamsExposeOnlySafeErrors(string data)
    {
        using var handler = new StreamingHttpHandler(new MemoryStream(Encoding.UTF8.GetBytes(data))); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(http).CompleteAsync([new(AgentRole.User, "q")], Token));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]); Assert.DoesNotContain("secret", error.Message);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("length", true)]
    [InlineData("content_filter", true)]
    [InlineData("stop", false)]
    public async Task TruncatedOrUnfinishedRepliesDoNotEnterHistory(string? finish, bool done)
    {
        string data = Event("""{"content":"partial"}""", finish) + (done ? "data: [DONE]\n\n" : "");
        using var handler = new StreamingHttpHandler(new MemoryStream(Encoding.UTF8.GetBytes(data))); using var http = new HttpClient(handler);
        var conversation = new AgentConversation(Create(http));
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.SendAsync("q", new("db", "", "", ""), Token));
        Assert.Empty(conversation.Messages);
    }

    [Fact]
    public async Task CancelAfterFirstDeltaStopsReadingAndKeepsSuccessfulHistoryEmpty()
    {
        using var stream = new ControlledSseStream(); using var handler = new StreamingHttpHandler(stream); using var http = new HttpClient(handler); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var conversation = new AgentConversation(Create(http));
        var pending = conversation.SendAsync("q", new("db", "", "", ""), cancel.Token, (_, _) => { received.TrySetResult(); return ValueTask.CompletedTask; });
        stream.Push(Event("""{"thinking":"received"}""")); await Wait(received.Task); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Empty(conversation.Messages);
        // 会话可能先响应取消；传输最终同样释放连接。
        await Wait(stream.Waiting.Task);
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("text/html")]
    public async Task NonSseResponsesAreRejectedInsteadOfPretendingToStream(string mediaType)
    {
        using var handler = new StreamingHttpHandler(new MemoryStream("{}"u8.ToArray())) { MediaType = mediaType }; using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(http).CompleteAsync([new(AgentRole.User, "q")], Token));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }

    [Theory]
    [InlineData("reasoning_content")]
    [InlineData("content")]
    public async Task ContentLimitsApplyBeforeFinalEvent(string field)
    {
        string data = Event(JsonSerializer.Serialize(new Dictionary<string, string> { [field] = new('x', 64001) }));
        using var handler = new StreamingHttpHandler(new MemoryStream(Encoding.UTF8.GetBytes(data))); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Create(http).CompleteAsync([new(AgentRole.User, "q")], Token));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }

    [Fact]
    public async Task ToolArgumentsAreAssembledAndExecutedOnlyAfterDone()
    {
        using var stream = new ControlledSseStream();
        using var handler = new StreamingHttpHandler(stream, new MemoryStream(Encoding.UTF8.GetBytes(Event("""{"content":"Observed result"}""", "stop") + "data: [DONE]\n\n"))); using var http = new HttpClient(handler);
        var conversation = new SshAgentConversation(Create(http)); int executed = 0;
        var pending = conversation.RunAsync("diagnose", "host", "", (_, _) => { executed++; return Task.FromResult(new SshAgentToolResult("completed", "bounded evidence")); }, _ => { }, Token);
        stream.Push(Event("""{"tool_calls":[{"index":0,"id":"call-1","type":"function","function":{"name":"ssh_diagnostic","arguments":"{\"operation\":"}}]}""")); await Wait(stream.Waiting.Task);
        Assert.Equal(0, executed); stream.ResetWaiting();
        stream.Push(Event("""{"tool_calls":[{"index":0,"function":{"arguments":"\"memory\",\"target\":\"\"}"}}]}""", "tool_calls")); await Wait(stream.Waiting.Task);
        Assert.Equal(0, executed); Assert.False(pending.IsCompleted);
        stream.Push("data: [DONE]\n\n"); await Wait(pending); Assert.Equal(1, executed);
    }

    [Fact]
    public async Task HeartbeatsResetIdleDeadlineButTotalDeadlineStillApplies()
    {
        using var stream = new ControlledSseStream(); using var handler = new StreamingHttpHandler(stream); using var http = new HttpClient(handler); var clock = new ManualAgentTimeProvider();
        var pending = Create(http, clock).CompleteAsync([new(AgentRole.User, "q")], Token);
        await Wait(stream.Waiting.Task);
        for (int minute = 1; minute < 15; minute++)
        {
            clock.Advance(TimeSpan.FromMinutes(1)); Assert.False(pending.IsCompleted);
            stream.ResetWaiting(); stream.Push(": heartbeat\n\n"); await Wait(stream.Waiting.Task);
        }
        clock.Advance(TimeSpan.FromMinutes(1));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal("AgentTimedOut", error.Data["Hermod.ResourceKey"]); Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task TwoMinutesWithoutBytesTimesOut()
    {
        using var stream = new ControlledSseStream(); using var handler = new StreamingHttpHandler(stream); using var http = new HttpClient(handler); var clock = new ManualAgentTimeProvider();
        var pending = Create(http, clock).CompleteAsync([new(AgentRole.User, "q")], Token); await Wait(stream.Waiting.Task);
        clock.Advance(TimeSpan.FromMinutes(2));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal("AgentTimedOut", error.Data["Hermod.ResourceKey"]);
    }
}
