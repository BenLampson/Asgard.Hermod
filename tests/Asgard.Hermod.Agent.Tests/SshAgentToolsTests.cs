using System.Text.Json;
using Asgard.Hermod.Agent;
using Xunit;

namespace Asgard.Hermod.Agent.Tests;

/// <summary>自动诊断只能来自固定模板，模型不能通过参数扩大到任意 Shell 能力。</summary>
public sealed class SshAgentToolsTests
{
    [Theory]
    [InlineData("host_summary", "", "uname -a; uptime")]
    [InlineData("disk_usage", "", "df -h")]
    [InlineData("memory", "", "free -m")]
    [InlineData("processes", "", "ps -eo pid,ppid,pcpu,pmem,comm --sort=-pcpu")]
    [InlineData("listening_ports", "", "ss -lntup")]
    [InlineData("service_status", "nginx.service", "systemctl --no-pager --full status -- 'nginx.service'")]
    [InlineData("service_logs", "postgresql@14-main", "journalctl --no-pager -n 100 -u 'postgresql@14-main'")]
    [InlineData("containers", "", "docker ps -a --no-trunc")]
    [InlineData("container_logs", "nginx-1", "docker logs --tail 100 --timestamps 'nginx-1'")]
    public void DiagnosticsUseFixedTemplates(string operation, string target, string expected)
    {
        var action = SshAgentTools.Parse(new("call", "ssh_diagnostic", JsonSerializer.Serialize(new { operation, target })));
        Assert.False(action.RequiresConfirmation); Assert.Equal(expected, action.Command);
        Assert.StartsWith("cd / && sh -c '", action.ExecutionCommand);
    }

    [Theory]
    [InlineData("nginx; reboot")]
    [InlineData("$(reboot)")]
    [InlineData("nginx' && reboot #")]
    [InlineData("--all")]
    [InlineData("nginx\nreboot")]
    [InlineData("*")]
    [InlineData("/etc/shadow")]
    [InlineData("")]
    public void UnsafeTargetsAreRejectedBeforeExecution(string target)
    {
        var error = Assert.Throws<InvalidOperationException>(() => SshAgentTools.Parse(new("call", "ssh_diagnostic", JsonSerializer.Serialize(new { operation = "service_logs", target }))));
        Assert.Equal("SshAgentInvalidTool", error.Data["Hermod.ResourceKey"]);
    }

    [Theory]
    [InlineData("{\"operation\":\"containers\",\"target\":\"\",\"command\":\"reboot\"}")]
    [InlineData("{\"operation\":\"service_logs\",\"target\":\"nginx\",\"target\":\"sshd\"}")]
    [InlineData("{\"operation\":\"reboot\",\"target\":\"\"}")]
    [InlineData("{\"operation\":\"containers\",\"target\":\"nginx\"}")]
    [InlineData("{\"operation\":\"containers\",\"target\":null}")]
    [InlineData("invalid")]
    public void InvalidDiagnosticsNeverBecomeArbitraryCommands(string arguments)
        => Assert.Throws<InvalidOperationException>(() => SshAgentTools.Parse(new("call", "ssh_diagnostic", arguments)));

    [Fact]
    public void ArbitraryCommandsAlwaysRequireConfirmationAndPreserveExactScript()
    {
        const string command = "printf '%s' 'hello'; systemctl restart nginx";
        var action = SshAgentTools.Parse(new("call", "ssh_command", JsonSerializer.Serialize(new { command })));
        Assert.True(action.RequiresConfirmation); Assert.Equal(command, action.Command);
        Assert.Equal("cd / && sh -c 'printf '\"'\"'%s'\"'\"' '\"'\"'hello'\"'\"'; systemctl restart nginx'", action.ExecutionCommand);
    }
}
