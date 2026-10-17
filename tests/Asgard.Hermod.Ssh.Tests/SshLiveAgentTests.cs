using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>在专用测试进程内验证 Windows 命名管道 Agent 的真实签名认证。</summary>
public sealed class SshLiveAgentTests
{
    /// <summary>Agent 测试需要用户显式准备的隔离容器，不启动 Windows 的用户服务。</summary>
    public static bool IsConfigured => OperatingSystem.IsWindows() && SshLiveConnectionTests.IsConfigured
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HERMOD_TEST_SSH_AGENT_CONTAINER"));

    /// <summary>选择 Agent 不读取密码或私钥，由命名管道提供身份和签名。</summary>
    [Fact(Skip = "Configure the isolated OpenSSH Agent fixture to enable this test.", SkipUnless = nameof(IsConfigured))]
    public async Task AgentSignsThroughIsolatedWindowsPipe()
    {
        await using var pipe = new LiveAgentPipe(SshLiveConnectionTests.Required("HERMOD_TEST_SSH_AGENT_CONTAINER"));
        string? previous = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", pipe.Address);
        try
        {
            var parameters = new Dictionary<string, string>(SshLiveConnectionTests.Options().Parameters)
            { ["auth"] = SshIds.Agent, ["password"] = "wrong-unused-password", ["key"] = "missing-unused-private-key" };
            var options = new InstanceConnectionOptions { TypeId = SshIds.Type, ProviderId = SshIds.Direct, Parameters = parameters };
            var trust = new LiveHostTrust(); using var timeout = SshLiveConnectionTests.Timeout();
            await using var connection = await new SshConnectionFactory(trust, trust).OpenAsync(options, 90, 30, timeout.Token);
            await SshLiveConnectionTests.RoundTripAsync(connection, timeout.Token);
        }
        finally { Environment.SetEnvironmentVariable("SSH_AUTH_SOCK", previous); }
    }
}
