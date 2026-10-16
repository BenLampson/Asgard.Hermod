using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证实际页面模型的异步连接、双向终端、取消、关闭和重连。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshWorkspaceTests(WpfTestFixture fixture)
{
    /// <summary>编辑名称同步共享导航对象，下次新会话使用新名称，现有传输不被重建。</summary>
    [Fact]
    public Task EditingNameUpdatesNavigationWithoutRestartingConnection() => fixture.RunOffscreenAsync(async () =>
    {
        var original = new InstanceSummary("ssh-test", "Original", "Original", "terminal", [], TypeId: SshIds.Type, ProviderId: SshIds.Direct);
        var instance = new InstanceItemViewModel(original); var factory = new SshTestFactory();
        var dialogs = new SshTestDialogs { Updated = original with { Name = "Renamed", DisplayName = "Renamed" } };
        await using var model = new SshWorkspaceViewModel(instance, new SshTestConfigurationService(), factory, dialogs, dialogs);
        await model.InitializeAsync(); var session = model.Session;
        await model.EditConnectionCommand.ExecuteAsync();
        Assert.Equal("Renamed", instance.Name); Assert.Equal("Renamed", model.Name); Assert.Equal("Renamed", model.Header);
        Assert.Same(session, model.Session); Assert.True(model.IsConnected); Assert.Equal(1, factory.Calls);
    });
    /// <summary>新增控制台只创建当前实例内的独立 PTY，关闭它不影响其他控制台。</summary>
    [Fact]
    public Task NewConsoleStaysInsideInstanceAndClosingKeepsOtherShells() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshWorkspaceTestConnection(); var factory = new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) }; var dialogs = new SshTestDialogs();
        await using var model = Create(factory, dialogs); await model.InitializeAsync(); var first = model.ActiveConsole!;
        await model.NewSessionCommand.ExecuteAsync(); var second = model.ActiveConsole!; Assert.NotSame(first, second); Assert.Equal(2, model.Consoles.Count); Assert.Equal(1, factory.Calls);
        first.Session.SendInput("first-command\r"u8); second.Session.SendInput("second-command\r"u8);
        await UntilAsync(() => connection.Consoles.All(item => item.Inputs.Count == 1));
        Assert.Equal("first-command\r", connection.Consoles[0].Inputs[0]); Assert.Equal("second-command\r", connection.Consoles[1].Inputs[0]);
        await model.CloseConsoleCommand.ExecuteAsync(first); Assert.Equal(2, model.Consoles.Count);
        dialogs.Accept = true; await model.CloseConsoleCommand.ExecuteAsync(first);
        Assert.Single(model.Consoles); Assert.Same(second, model.ActiveConsole); Assert.True(model.IsConnected); Assert.Equal(0, connection.DisposeCount);
        Assert.Equal(1, connection.Consoles[0].DisposeCount); Assert.Equal(0, connection.Consoles[1].DisposeCount);
    });
    /// <summary>初始化幂等、UTF-8 原样显示、输入按序发送并更新远端 PTY。</summary>
    [Fact]
    public Task ConnectedSessionTransfersBytesAndResizes() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory(); await using var model = Create(factory);
        await model.InitializeAsync(); await model.InitializeAsync();
        Assert.Equal(1, factory.Calls); Assert.True(model.IsConnected); Assert.False(model.IsReadOnly);
        var connection = Assert.Single(factory.Connections); connection.Output("远程输出\r\ntester@host $ ");
        await UntilAsync(() => model.Session!.GetVisibleText().Contains("远程输出", StringComparison.Ordinal));
        model.Session!.SendInput("echo 中文\r"u8); model.Session!.SendInput("next\r"u8);
        await UntilAsync(() => connection.Inputs.Count == 2);
        Assert.Equal(new[] { "echo 中文\r", "next\r" }, connection.Inputs);
        model.Session!.Resize(100, 30); Assert.Equal((100, 30), connection.Size);
    });

    /// <summary>断线保留历史，显式重连替换缓冲区且不重放旧输入。</summary>
    [Fact]
    public Task ReconnectPreservesOldHistoryUntilSuccessAndNeverReplaysInput() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory(); await using var model = Create(factory);
        await model.InitializeAsync(); var original = model.Session!; var first = factory.Connections[0];
        first.Output("retained output"); await UntilAsync(() => original.GetVisibleText().Contains("retained output", StringComparison.Ordinal));
        original.SendInput("old-command\r"u8); await UntilAsync(() => first.Inputs.Count == 1);
        await model.DisconnectCommand.ExecuteAsync();
        Assert.True(model.IsReadOnly); Assert.Same(original, model.Session); Assert.Contains("retained output", model.Session!.GetVisibleText());
        Assert.Equal(1, first.DisposeCount);
        await model.ReconnectCommand.ExecuteAsync(); Assert.True(model.IsConnected); Assert.NotSame(original, model.Session);
        Assert.False(original.IsDisposed); Assert.Equal(2, model.Consoles.Count); Assert.True(model.Consoles[0].IsReadOnly); Assert.Empty(factory.Connections[1].Inputs);
    });

    /// <summary>用户取消后到达的连接被释放，不能恢复输入或成为活动会话。</summary>
    [Fact]
    public Task LateConnectionAfterCancelIsDisposed() => fixture.RunOffscreenAsync(async () =>
    {
        var completion = new TaskCompletionSource<ISshConnection>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new SshTestFactory { Handler = _ => completion.Task }; await using var model = Create(factory);
        Task opening = model.InitializeAsync(); model.CancelConnectionCommand.Execute(null);
        Assert.True(factory.LastToken.IsCancellationRequested);
        var late = new SshTestConnection(); completion.SetResult(late); await opening;
        Assert.True(model.IsEnded); Assert.True(model.IsReadOnly); Assert.Equal(1, late.DisposeCount);
    });

    /// <summary>失败重连不替换历史，反馈不含连接库的原始异常。</summary>
    [Fact]
    public Task FailedReconnectRetainsHistoryAndSafeError() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory(); await using var model = Create(factory); await model.InitializeAsync();
        var original = model.Session!; factory.Connections[0].Output("history");
        await UntilAsync(() => original.GetVisibleText().Contains("history", StringComparison.Ordinal));
        await model.DisconnectCommand.ExecuteAsync();
        factory.Handler = _ => Task.FromException<ISshConnection>(new InvalidOperationException("password=test-only"));
        await model.ReconnectCommand.ExecuteAsync(); Assert.Equal(SshWorkspacePhase.Failed, model.Phase);
        Assert.Same(original, model.Session); Assert.DoesNotContain("test-only", model.Error);
        Assert.Equal("SSH_CONNECTION_FAILED", model.ErrorCode);
    });

    /// <summary>拒绝关闭不释放会话，远端退出后关闭不再要求确认。</summary>
    [Fact]
    public Task ActiveCloseRequiresConfirmationWhileEndedCloseDoesNot() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory(); var dialogs = new SshTestDialogs(); await using var model = Create(factory, dialogs);
        await model.InitializeAsync(); Assert.False(await model.RequestCloseAsync());
        Assert.True(model.IsConnected); Assert.Equal(0, factory.Connections[0].DisposeCount);
        dialogs.Accept = true; Assert.True(await model.RequestCloseAsync()); Assert.Equal(2, dialogs.Calls);
        factory.Connections[0].End(); await UntilAsync(() => model.IsEnded);
        Assert.True(await model.RequestCloseAsync()); Assert.Equal(2, dialogs.Calls);
    });

    /// <summary>关闭连接中的页面会取消协议边界，后续初始化不能创建新连接。</summary>
    [Fact]
    public Task DisposeCancelsPendingConnectionAndUnbindsSession() => fixture.RunOffscreenAsync(async () =>
    {
        var factory = new SshTestFactory { Handler = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return new SshTestConnection(); } };
        var model = Create(factory); Task opening = model.InitializeAsync(); await model.DisposeAsync(); await opening;
        Assert.True(factory.LastToken.IsCancellationRequested); Assert.Null(model.Session); Assert.True(model.IsClosed);
        await model.InitializeAsync(); Assert.Equal(1, factory.Calls);
    });

    internal static SshWorkspaceViewModel Create(SshTestFactory factory, SshTestDialogs? dialogs = null)
    {
        dialogs ??= new();
        return new(new InstanceItemViewModel(new("ssh-test", "Production SSH", "Production SSH", "terminal", [],
            TypeId: SshIds.Type, ProviderId: SshIds.Direct)), new SshTestConfigurationService(), factory, dialogs, dialogs);
    }

    /// <summary>轮询异步协议结果有明确截止时间，不依赖固定睡眠碰运气。</summary>
    internal static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!condition()) { Assert.True(DateTime.UtcNow < deadline, "SSH state did not settle."); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
}
