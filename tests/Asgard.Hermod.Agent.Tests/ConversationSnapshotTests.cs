using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>历史只恢复问答，不携带旧工具、上下文或执行授权。</summary>
public sealed class ConversationSnapshotTests
{
    [Fact]
    public async Task RestoreRoundTripsSuccessfulMessagesAndKeepsContextTransient()
    {
        var client = new Client(); var context = new AgentSqlContext("app", "old-private-metadata", "", ScopeId: "scope");
        var sql = new AgentConversation(client); await sql.SendAsync("question", context, TestContext.Current.CancellationToken);
        var saved = sql.Capture(); sql.Clear(); sql.Restore(saved);
        await sql.SendAsync("next", context with { Schema = "current-metadata" }, TestContext.Current.CancellationToken);
        Assert.Contains(client.Last!, message => message.Content == "question");
        Assert.DoesNotContain(client.Last!, message => message.Content.Contains("old-private-metadata", StringComparison.Ordinal));
        var ssh = new SshAgentConversation(client); await ssh.SendAsync("diagnose", "host", "old-output", TestContext.Current.CancellationToken);
        var sshSaved = ssh.Capture(); ssh.Clear(); ssh.Restore(sshSaved);
        Assert.Equal(2, ssh.Capture().Messages.Count); Assert.DoesNotContain(ssh.Capture().Messages, message => message.Content.Contains("old-output", StringComparison.Ordinal));
        var logs = new LogAnalysisConversation(client); await logs.SendAsync("analyze", "host", "old-log", null, false, TestContext.Current.CancellationToken);
        var logSaved = logs.Capture(); logs.Clear(); logs.Restore(logSaved); Assert.Equal(logSaved.Messages, logs.Capture().Messages);
    }

    [Theory]
    [InlineData(AgentRole.System)]
    [InlineData(AgentRole.Tool)]
    public void RestoreRejectsInjectedRolesAcrossAllEngines(AgentRole role)
    {
        var client = new Client(); var snapshot = new AgentConversationSnapshot([new(role, "not a successful user question"), new(AgentRole.Assistant, "answer")]);
        Assert.ThrowsAny<Exception>(() => new AgentConversation(client).Restore(snapshot));
        Assert.ThrowsAny<Exception>(() => new SshAgentConversation(client).Restore(snapshot));
        Assert.ThrowsAny<Exception>(() => new LogAnalysisConversation(client).Restore(snapshot));
    }

    [Fact]
    public async Task BusyHistoryCannotBeReplacedAndCanceledClientCannotAppendLateReply()
    {
        var client = new Client { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var conversation = new SshAgentConversation(client); using var token = new CancellationTokenSource();
        var running = conversation.SendAsync("old", "host", "", token.Token);
        Assert.ThrowsAny<Exception>(() => conversation.Restore(AgentConversationSnapshot.Empty));
        token.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        conversation.Restore(AgentConversationSnapshot.Empty); client.Pending.SetResult(AgentReply.FromContent("late"));
        Assert.Empty(conversation.Capture().Messages);
    }

    [Fact]
    public async Task MessageModelIdentityUsesResponseModelOrCapturedRequestFallback()
    {
        using var handler = new AgentHttpHandler(); using var http = new HttpClient(handler); var config = new AgentConfiguration();
        config.Update("https://example.com/v1", "requested", "test-only");
        var client = new OpenAiCompatibleAgentClient(http, config);
        handler.Response = """{"model":"actual-deployment","choices":[{"finish_reason":"stop","message":{"role":"assistant","content":"answer"}}]}""";
        var first = await client.CompleteAsync([new(AgentRole.User, "question")], TestContext.Current.CancellationToken);
        config.Update("https://example.com/v1", "new-default", "test-only"); Assert.Equal("actual-deployment", first.ModelIdentity);
        handler.Response = null; var fallback = await client.CompleteAsync([new(AgentRole.User, "question")], TestContext.Current.CancellationToken); Assert.Equal("new-default", fallback.ModelIdentity);
    }

    private sealed class Client : IAgentClient
    {
        internal IReadOnlyList<AgentMessage>? Last;
        internal TaskCompletionSource<AgentReply>? Pending;
        public Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default)
        {
            Last = messages.ToArray(); return Pending?.Task ?? Task.FromResult(AgentReply.FromContent("answer"));
        }
    }
}
