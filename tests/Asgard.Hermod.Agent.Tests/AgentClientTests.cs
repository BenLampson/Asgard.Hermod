using System.Net;
using System.Text.Json;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

public sealed class AgentClientTests
{
    [Theory]
    [InlineData("https://models.example/v1", "https://models.example/v1/chat/completions")]
    [InlineData("https://models.example/v1/chat/completions/", "https://models.example/v1/chat/completions")]
    public async Task Request_UsesConfiguredModelAndBearerKeyWithoutSendingKeyInBody(string endpoint, string expected)
    {
        using var handler = new AgentHttpHandler(); using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update(endpoint, "user-selected-model", "private-key");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var reply = await client.CompleteAsync([new(AgentRole.User, "Explain SELECT 1")], TestContext.Current.CancellationToken);
        Assert.Equal(expected, handler.Endpoints[0]!.AbsoluteUri); Assert.Equal("private-key", handler.Keys[0]);
        using var body = JsonDocument.Parse(handler.Bodies[0]); var root = body.RootElement;
        Assert.Equal("user-selected-model", root.GetProperty("model").GetString());
        Assert.False(root.GetProperty("store").GetBoolean()); Assert.False(root.GetProperty("stream").GetBoolean());
        Assert.Equal("user", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.DoesNotContain("private-key", handler.Bodies[0]); Assert.False(root.TryGetProperty("tools", out _));
        Assert.Equal("SELECT 1;", Assert.Single(reply.SqlBlocks));
    }

    [Fact]
    public async Task ConfigurationChange_IsPickedUpBetweenRequestsAndAllowsNoKey()
    {
        using var handler = new AgentHttpHandler(); using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model-one", "first-key");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        await client.CompleteAsync([new(AgentRole.User, "first")], TestContext.Current.CancellationToken);
        configuration.Update("http://localhost:1234/v1", "model-two", "");
        await client.CompleteAsync([new(AgentRole.User, "second")], TestContext.Current.CancellationToken);
        Assert.Null(handler.Keys[1]); Assert.Contains("model-two", handler.Bodies[1]); Assert.DoesNotContain("first-key", handler.Bodies[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AgentAuthenticationFailed")]
    [InlineData(HttpStatusCode.Forbidden, "AgentAuthenticationFailed")]
    [InlineData(HttpStatusCode.TooManyRequests, "AgentRateLimited")]
    [InlineData(HttpStatusCode.InternalServerError, "AgentRequestFailed")]
    [InlineData(HttpStatusCode.Redirect, "AgentRequestFailed")]
    public async Task HttpFailure_ReturnsSafeResourceWithoutRawServiceData(HttpStatusCode status, string key)
    {
        using var handler = new AgentHttpHandler { Status = status, Response = "raw-secret-response" }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "private-key");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync([new(AgentRole.User, "query")], TestContext.Current.CancellationToken));
        Assert.Equal(key, error.Data["Hermod.ResourceKey"]); Assert.DoesNotContain("raw-secret-response", error.ToString());
        Assert.DoesNotContain("private-key", error.ToString()); Assert.Single(handler.Bodies);
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("{\"choices\":[]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"length\",\"message\":{\"content\":\"```sql SELECT\"}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":null}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"\"}}]}")]
    public async Task InvalidResponse_RejectsMalformedTruncatedAndToolOnlyReplies(string response)
    {
        using var handler = new AgentHttpHandler { Response = response }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync([new(AgentRole.User, "query")], TestContext.Current.CancellationToken));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }

    [Fact]
    public async Task Cancellation_InterruptsPendingHttpAndIsNotReportedAsModelFailure()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new AgentHttpHandler { BeforeResponse = async token => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); } };
        using var http = new HttpClient(handler); using var cancellation = new CancellationTokenSource();
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var pending = client.CompleteAsync([new(AgentRole.User, "query")], cancellation.Token);
        await started.Task; cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); Assert.Single(handler.Bodies);
    }

    [Fact]
    public async Task OversizedResponse_StopsBeforeAcceptingContent()
    {
        using var handler = new AgentHttpHandler { Response = new string('x', 1024 * 1024 + 1) }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync([new(AgentRole.User, "query")], TestContext.Current.CancellationToken));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }
}
