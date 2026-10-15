using System.Text.Json;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>工具协议仅由显式 SSH 请求启用；SQL 等现有文本路径维持原行为。</summary>
public sealed class AgentToolClientTests
{
    /// <summary>格式错误的工具协议只产生安全错误，不暴露模型响应或进入执行阶段。</summary>
    [Theory]
    [InlineData("{\"choices\":[null]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":null,\"tool_calls\":[{}]}}]}")]
    [InlineData("{\"choices\":[{\"finish_reason\":\"tool_calls\",\"message\":{\"content\":42,\"tool_calls\":[{}]}}]}")]
    public async Task MalformedToolResponseUsesSafeResource(string response)
    {
        using var handler = new AgentHttpHandler { Response = response }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiCompatibleAgentClient(http, configuration).CompleteWithToolsAsync([new(AgentRole.User, "diagnose")], SshAgentTools.Definitions, TestContext.Current.CancellationToken));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }
    private static readonly string ToolResponse = JsonSerializer.Serialize(new
    {
        choices = new[] { new { finish_reason = "tool_calls", message = new { content = (string?)null, tool_calls = new[] { new { id = "call_one", type = "function", function = new { name = "ssh_diagnostic", arguments = "{\"operation\":\"memory\",\"target\":\"\"}" } } } } } },
    });

    [Fact]
    public async Task ToolOnlyReplyAndResultsPreserveProtocolAssociation()
    {
        using var handler = new AgentHttpHandler { Response = ToolResponse }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "private-key");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var reply = await client.CompleteWithToolsAsync([new(AgentRole.User, "diagnose")], SshAgentTools.Definitions, TestContext.Current.CancellationToken);
        var call = Assert.Single(reply.ToolCalls); Assert.Equal("call_one", call.Id); Assert.Empty(reply.Content);
        handler.Response = null;
        await client.CompleteWithToolsAsync([new(AgentRole.User, "diagnose"), new(AgentRole.Assistant, "") { ToolCalls = reply.ToolCalls }, new(AgentRole.Tool, "bounded result") { ToolCallId = call.Id }], [], TestContext.Current.CancellationToken);
        using var first = JsonDocument.Parse(handler.Bodies[0]);
        Assert.Equal(2, first.RootElement.GetProperty("tools").GetArrayLength()); Assert.False(first.RootElement.GetProperty("parallel_tool_calls").GetBoolean());
        Assert.DoesNotContain("private-key", handler.Bodies[0]);
        using var second = JsonDocument.Parse(handler.Bodies[1]); var messages = second.RootElement.GetProperty("messages");
        Assert.Equal("call_one", messages[1].GetProperty("tool_calls")[0].GetProperty("id").GetString());
        Assert.Equal("tool", messages[2].GetProperty("role").GetString()); Assert.Equal("call_one", messages[2].GetProperty("tool_call_id").GetString());
        Assert.Equal("none", second.RootElement.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task TextOnlyAndFinalAnalysisRequestsRejectToolExecution()
    {
        using var handler = new AgentHttpHandler { Response = ToolResponse }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var textError = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync([new(AgentRole.User, "SQL")], TestContext.Current.CancellationToken));
        var finalError = await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteWithToolsAsync([new(AgentRole.User, "summarize")], [], TestContext.Current.CancellationToken));
        Assert.Equal("AgentInvalidResponse", textError.Data["Hermod.ResourceKey"]); Assert.Equal("AgentInvalidResponse", finalError.Data["Hermod.ResourceKey"]);
    }

    [Theory]
    [InlineData("length", "ssh_diagnostic")]
    [InlineData("stop", "ssh_diagnostic")]
    [InlineData("tool_calls", "unknown_tool")]
    public async Task TruncatedOrUndeclaredToolRequestsAreRejected(string reason, string name)
    {
        using var handler = new AgentHttpHandler { Response = ToolResponse.Replace("tool_calls\",\"message", reason + "\",\"message", StringComparison.Ordinal).Replace("ssh_diagnostic", name, StringComparison.Ordinal) }; using var http = new HttpClient(handler);
        var configuration = new AgentConfiguration(); configuration.Update("https://models.example/v1", "model", "");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenAiCompatibleAgentClient(http, configuration).CompleteWithToolsAsync([new(AgentRole.User, "diagnose")], SshAgentTools.Definitions, TestContext.Current.CancellationToken));
        Assert.Equal("AgentInvalidResponse", error.Data["Hermod.ResourceKey"]);
    }
}
