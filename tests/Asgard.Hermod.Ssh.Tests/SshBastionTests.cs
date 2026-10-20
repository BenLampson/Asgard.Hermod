using System.Text;
using System.Text.RegularExpressions;
using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.Ssh.Tests;

/// <summary>覆盖菜单型堡垒机的配置、分段提示符、独立工具选机和失败边界。</summary>
public sealed class SshBastionTests
{
    /// <summary>菜单会话结束后，迟到的 UI 通知仍可读取缓存信号，而不访问已释放的网关。</summary>
    [Fact]
    public async Task DisconnectedSignalRemainsReadableAfterDispose()
    {
        var transport = new BastionTestConnection();
        var connection = await SshBastionConnection.CreateAsync(transport, Options, TestContext.Current.CancellationToken);
        var signal = connection.Disconnected; Assert.True(signal.CanBeCanceled);
        await connection.DisposeAsync();
        Assert.Equal(signal, connection.Disconnected); Assert.True(connection.Disconnected.IsCancellationRequested);
        await connection.DisposeAsync();
    }

    internal static SshConnectionOptions Options => SshConnectionOptions.Parse(Connection());
    private static InstanceConnectionOptions Connection(string selection = "2") => SshOptionsTests.Connection(SshIds.Bastion,
        new Dictionary<string, string> { ["host"] = "bastion.example", ["user"] = "tester", ["auth"] = SshIds.Agent, ["bastion.selection"] = selection });

    /// <summary>只需堡垒机身份和编号，不要求目标服务器的地址或第二套凭据。</summary>
    [Fact]
    public void MenuRouteUsesOneIdentityAndSeparateStableSettings()
    {
        var options = Options;
        Assert.Null(options.JumpHost); Assert.Equal("2", options.BastionSelection); Assert.Equal("[vbh]>", options.BastionMenuPrompt);
        Assert.DoesNotContain(SshProfile.Fields(SshIds.Bastion), field => field.Key.StartsWith("jump.", StringComparison.Ordinal));
        Assert.Equal("002", SshConnectionOptions.Parse(Connection("002")).BastionSelection);
    }

    /// <summary>编号不能变为 Shell 命令、控制键或任意自动输入脚本。</summary>
    [Theory]
    [InlineData("")]
    [InlineData("1; id")]
    [InlineData("1\rwhoami")]
    [InlineData("１２")]
    [InlineData("-1")]
    [InlineData("1234567890")]
    public void InvalidSelectionsAreRejectedBeforeConnecting(string selection) =>
        Assert.ThrowsAny<Exception>(() => SshConnectionOptions.Parse(Connection(selection)));

    /// <summary>菜单拆包或带颜色时等待完整提示符，发送一次编号并保留原始登录输出。</summary>
    [Fact]
    public async Task SelectWaitsForPromptAndPreservesTranscript()
    {
        var console = new BastionTestConsole();
        console.Output("\u001b[32mWELCOME\u001b[0m\r\n[vb");
        var selected = SshBastionConsole.SelectAsync(console, Options, TestContext.Current.CancellationToken);
        Assert.Empty(console.Inputs);
        console.Written = value => { Assert.Equal("2\r", value); console.Output("2\r\nroot@target:~"); console.Output("# \u001b[0m"); };
        console.Output("h]> ");
        await using var ready = await selected;
        byte[] bytes = new byte[4096]; int count = await ready.ReadAsync(bytes, TestContext.Current.CancellationToken);
        Assert.Contains("WELCOME", Encoding.UTF8.GetString(bytes, 0, count));
        Assert.Contains("root@target", Encoding.UTF8.GetString(bytes, 0, count)); Assert.Single(console.Inputs);
    }

    /// <summary>编号被拒绝时不把后续监控命令发送到菜单，也不尝试其他编号。</summary>
    [Fact]
    public async Task RejectedNumberDisposesOnlyItsConsole()
    {
        var console = new BastionTestConsole { Written = _ => { } };
        console.Output("[vbh]> "); console.Written = _ => console.Output("Access denied\r\n[vbh]> ");
        var error = await Assert.ThrowsAsync<SshException>(() => SshBastionConsole.SelectAsync(console, Options, TestContext.Current.CancellationToken));
        Assert.Equal("SSH_BASTION_SELECTION_FAILED", error.Code); Assert.True(console.Disposed); Assert.Equal(new[] { "2\r" }, console.Inputs);
        Assert.DoesNotContain("Access denied", error.Message);
    }

    /// <summary>取消菜单等待后不能发送迟到的编号。</summary>
    [Fact]
    public async Task CancelBeforeMenuNeverSendsSelection()
    {
        var console = new BastionTestConsole(); using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = SshBastionConsole.SelectAsync(console, Options, cancel.Token); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending); console.Output("[vbh]> ");
        Assert.True(console.Disposed); Assert.Empty(console.Inputs);
    }

    /// <summary>每条工具命令重新选机，用户终端不会收到命令；终端改行不会改变编码后的原始结果。</summary>
    [Fact]
    public async Task ToolsSelectTheirOwnTargetAndPreserveOutputAndExitStatus()
    {
        var transport = new BastionTestConnection();
        transport.Written = (console, command) =>
        {
            string id = Regex.Match(command, "([0-9a-f]{32})_BEGIN", RegexOptions.CultureInvariant).Groups[1].Value;
            Assert.NotEmpty(id);
            console.Output(command.Replace("\n", "\r\n", StringComparison.Ordinal));
            string output = "中文\r\n\u001b[31mquoted ' output\u001b[0m\n";
            string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(output + "\n__HERMOD_" + id + "_STATUS:42\n"));
            console.Output("\r\n__HERMOD_" + id + "_BEGIN\r\n" + encoded + "\r\n\r\n__HERMOD_" + id + "_END\r\n");
        };
        await using var connection = await SshBastionConnection.CreateAsync(transport, Options, TestContext.Current.CancellationToken);
        for (int i = 0; i < 2; i++)
        {
            var result = await connection.ExecuteCommandAsync("printf '%s' \"quotes ' and 中文\"; exit 42", TestContext.Current.CancellationToken);
            Assert.Equal("中文\r\n\u001b[31mquoted ' output\u001b[0m\n", result.Output); Assert.Equal(42, result.ExitCode); Assert.False(result.Truncated);
        }
        Assert.Equal(new[] { "2\r" }, transport.Consoles[0].Inputs); Assert.False(transport.Consoles[0].Disposed);
        Assert.Equal(3, transport.Consoles.Count);
        Assert.All(transport.Consoles.Skip(1), item => { Assert.Equal("2\r", item.Inputs[0]); Assert.Equal(2, item.Inputs.Count); Assert.True(item.Disposed); });
        var error = await Assert.ThrowsAsync<SshException>(() => connection.ForwardAsync(13306, "db.internal", 3306, TestContext.Current.CancellationToken));
        Assert.Equal("SSH_BASTION_TOOLS_UNSUPPORTED", error.Code);
    }

    /// <summary>缺少完整协议帧的响应不能被误报为成功或泄漏网关原始响应。</summary>
    [Fact]
    public async Task MalformedCommandOutputFailsWithoutRetry()
    {
        var command = new SshBastionCommand(); var console = new BastionTestConsole();
        console.Written = _ => console.Output("\n" + command.Begin + "\nsecret server response\n" + command.End + "\n");
        var error = await Assert.ThrowsAsync<SshException>(() => command.ExecuteAsync(console, "true", TestContext.Current.CancellationToken));
        Assert.Equal("SSH_BASTION_COMMAND_FAILED", error.Code); Assert.DoesNotContain("secret", error.Message); Assert.Single(console.Inputs);
    }

    /// <summary>已经发送的命令取消后只关闭自己的通道，不重放命令或中断用户终端。</summary>
    [Fact]
    public async Task CancelCommandDisposesToolConsoleAndLeavesUserConsoleAlive()
    {
        var transport = new BastionTestConnection();
        var dispatched = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.Written = (_, _) => dispatched.TrySetResult();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using var connection = await SshBastionConnection.CreateAsync(transport, Options, cancel.Token);
        var pending = connection.ExecuteCommandAsync("sleep 100", cancel.Token);
        await dispatched.Task.WaitAsync(TestContext.Current.CancellationToken); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(2, transport.Consoles.Count); Assert.False(transport.Consoles[0].Disposed);
        Assert.True(transport.Consoles[1].Disposed); Assert.Equal(2, transport.Consoles[1].Inputs.Count);
    }
}
