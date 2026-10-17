using System.Text.Json;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>验证工具结果自动回传、有限执行、拒绝后分析和连接代次隔离。</summary>
public sealed class SshTroubleshootingTests
{
    [Fact]
    public async Task ResultsAreLinkedToCallsAndUsedByNextAnalysisButNotNextTask()
    {
        var client = new Client([Diagnostic("one"), AgentReply.FromContent("Found the cause"), AgentReply.FromContent("New task")]);
        var conversation = new SshAgentConversation(client); var executed = new List<SshAgentAction>();
        await conversation.RunAsync("diagnose", "host", "attached", (action, _) => { executed.Add(action); return Task.FromResult(new SshAgentToolResult("completed", "private-log-for-this-task", 3)); }, _ => { }, TestContext.Current.CancellationToken);
        Assert.Single(executed); var result = Assert.Single(client.Requests[1], message => message.Role == AgentRole.Tool);
        Assert.Equal("one", result.ToolCallId); Assert.Contains("private-log-for-this-task", result.Content); Assert.Contains("\"exitCode\":3", result.Content);
        Assert.Equal("one", Assert.Single(client.Requests[1].Single(message => message.Role == AgentRole.Assistant).ToolCalls).Id);
        await conversation.RunAsync("fresh", "host", "", (_, _) => throw new InvalidOperationException(), _ => { }, TestContext.Current.CancellationToken);
        Assert.DoesNotContain("private-log-for-this-task", string.Join("", client.Requests[2].Select(message => message.Content)));
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("unknown")]
    public async Task DeniedOrUnknownOutcomesAllowOnlyFinalAnalysis(string status)
    {
        var client = new Client([Diagnostic("one"), AgentReply.FromContent("Stopped safely")]);
        var conversation = new SshAgentConversation(client);
        await conversation.RunAsync("diagnose", "host", "", (_, _) => Task.FromResult(new SshAgentToolResult(status, "", StopActions: true)), _ => { }, TestContext.Current.CancellationToken);
        Assert.Empty(client.Tools[1]); Assert.Contains(status, client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
    }

    [Fact]
    public async Task EightStepLimitForcesSummaryAndCannotBeOverriddenByModel()
    {
        var client = new Client(Enumerable.Range(0, 9).Select(index => Diagnostic(index.ToString())).ToArray()); int count = 0;
        var conversation = new SshAgentConversation(client);
        await Assert.ThrowsAsync<InvalidOperationException>(() => conversation.RunAsync("diagnose", "host", "", (_, _) => { count++; return Task.FromResult(new SshAgentToolResult("completed", "ok", 0)); }, _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal(8, count); Assert.Empty(client.Tools[^1]);
    }

    [Fact]
    public async Task DuplicateCallIdCannotExecuteTwice()
    {
        var client = new Client([Diagnostic("same"), Diagnostic("same")]); int count = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SshAgentConversation(client).RunAsync("diagnose", "host", "", (_, _) => { count++; return Task.FromResult(new SshAgentToolResult("completed", "ok")); }, _ => { }, TestContext.Current.CancellationToken));
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ReconnectWhileModelIsThinkingCannotExecuteLateTool()
    {
        var completion = new TaskCompletionSource<AgentReply>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new Client([]) { Pending = completion.Task }; var conversation = new SshAgentConversation(client); int count = 0;
        var pending = conversation.RunAsync("diagnose", "old", "", (_, _) => { count++; return Task.FromResult(new SshAgentToolResult("completed", "ok")); }, _ => { }, TestContext.Current.CancellationToken);
        conversation.Clear(); completion.SetResult(Diagnostic("late"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending); Assert.Equal(0, count);
    }

    [Fact]
    public async Task OutputTruncationIsExplicitInModelFeedback()
    {
        var client = new Client([Diagnostic("one"), AgentReply.FromContent("Partial result")]);
        await new SshAgentConversation(client).RunAsync("diagnose", "host", "", (_, _) => Task.FromResult(new SshAgentToolResult("completed", new string('x', 5000), 0)), _ => { }, TestContext.Current.CancellationToken);
        using var data = JsonDocument.Parse(client.Requests[1].Single(message => message.Role == AgentRole.Tool).Content);
        Assert.True(data.RootElement.GetProperty("truncated").GetBoolean()); Assert.Equal(4000, data.RootElement.GetProperty("output").GetString()!.Length);
    }

    private static AgentReply Diagnostic(string id) => new("", []) { ToolCalls = [new(id, "ssh_diagnostic", "{\"operation\":\"containers\",\"target\":\"\"}")] };
    private sealed class Client(IReadOnlyList<AgentReply> replies) : IAgentClient
    {
        internal List<AgentMessage[]> Requests { get; } = [];
        internal List<AgentToolDefinition[]> Tools { get; } = [];
        internal Task<AgentReply>? Pending { get; init; }
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        { Requests.Add(messages.ToArray()); Tools.Add(tools.ToArray()); return Pending ?? Task.FromResult(replies[Requests.Count - 1]); }
    }
}
