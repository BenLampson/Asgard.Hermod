using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>显式启用的模型兼容性验证，只发送合成上下文，密钥从当前进程环境读取。</summary>
public sealed class LiveAgentTests
{
    /// <summary>未由调用者提供三个测试变量时不访问网络。</summary>
    public static bool IsConfigured => new[] { "HERMOD_AGENT_TEST_ENDPOINT", "HERMOD_AGENT_TEST_MODEL", "HERMOD_AGENT_TEST_KEY" }
        .All(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));

    /// <summary>验证真实服务的兼容协议、响应解析和 SQL 建议，完全不连接用户数据库。</summary>
    [Fact(Skip = "Provide HERMOD_AGENT_TEST_ENDPOINT, HERMOD_AGENT_TEST_MODEL and HERMOD_AGENT_TEST_KEY to enable the isolated model smoke test.", SkipUnless = nameof(IsConfigured))]
    public async Task ModelService_ReturnsSqlSuggestionForSyntheticContext()
    {
        var configuration = new AgentConfiguration();
        configuration.Update(Environment.GetEnvironmentVariable("HERMOD_AGENT_TEST_ENDPOINT")!,
            Environment.GetEnvironmentVariable("HERMOD_AGENT_TEST_MODEL")!, Environment.GetEnvironmentVariable("HERMOD_AGENT_TEST_KEY")!);
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false };
        using var http = new HttpClient(handler);
        var client = new OpenAiCompatibleAgentClient(http, configuration);
        var conversation = new AgentConversation(client);
        var reply = await conversation.SendAsync("Reply with one fenced sql block containing SELECT 1; and no explanation.",
            new("", "", ""), TestContext.Current.CancellationToken);
        Assert.NotEmpty(reply.SqlBlocks);
        Assert.Contains("SELECT", reply.SqlBlocks[0], StringComparison.OrdinalIgnoreCase);
        configuration.Update("", "", "");
    }
}
