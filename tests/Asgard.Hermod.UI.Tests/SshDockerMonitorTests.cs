using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>资源刷新失败与工作区日志跟踪的实际生命周期验收。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshDockerMonitorTests(WpfTestFixture fixture)
{
    /// <summary>首次采样尚未结束时，显式刷新必须排队，返回资源监测后保留指定容器。</summary>
    [Fact]
    public Task FirstSamplingDoesNotDropTheRequestedDockerRefresh() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var firstSample = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool waiting = true;
        connection.Handler = (command, token) =>
        {
            if (waiting && command.Contains("__CPU__", StringComparison.Ordinal)) { waiting = false; return firstSample.Task; }
            return data.HandleAsync(command, token);
        };
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection);
        model.IsPaused = true;
        string expected = new('b', 64); model.SelectDockerContainer(expected);
        var refresh = model.RefreshDetailsCommand.ExecuteAsync();
        Assert.False(refresh.IsCompleted);
        firstSample.TrySetResult(new(SshMonitorTestData.Sample(1), 0, false)); await refresh;
        Assert.Empty(model.SectionError); Assert.Equal(expected, model.SelectedContainer!.Id);
        Assert.NotNull(model.SelectedContainer.Resources);
    });

    /// <summary>断线会取消排队的刷新，不遗留等待，也不能把旧资源写回新连接。</summary>
    [Fact]
    public Task DisconnectCancelsAQueuedExplicitRefresh() => fixture.RunOffscreenAsync(async () =>
    {
        var firstSample = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = new SshWorkspaceTestConnection { Handler = (_, _) => firstSample.Task };
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection);
        model.Section = "docker"; var refresh = model.RefreshDetailsCommand.ExecuteAsync();
        Assert.False(refresh.IsCompleted); await model.StopAsync(); await refresh;
        firstSample.TrySetResult(new(SshMonitorTestData.Sample(1), 0, false));
        Assert.Empty(model.Containers); Assert.Null(model.SelectedContainer);
    });

    /// <summary>失败保留旧值但新增历史断点，恢复后重新建立速率基线。</summary>
    [Fact]
    public Task FailedStatsRemainExplicitlyStaleAndRecoveryResetsRates() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool fail = false;
        connection.Handler = (command, token) => fail && command.StartsWith("LC_ALL=C docker stats ", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult("private token=never-show", 1, false)) : data.HandleAsync(command, token);
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection); model.IsPaused = true; model.Section = "docker";
        await model.RefreshDetailsCommand.ExecuteAsync(); model.SelectedContainer = model.Containers.First(c => c.Name == "hermod-api");
        var old = model.SelectedContainer.Resources; fail = true; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Same(old, model.SelectedContainer!.Resources); Assert.NotEmpty(model.SectionError); Assert.DoesNotContain("private", model.BannerText, StringComparison.Ordinal);
        Assert.Null(model.ContainerCpuPoints.Last().Value); Assert.Null(model.ContainerMemoryPoints.Last().Value);
        fail = false; await model.RefreshDetailsCommand.ExecuteAsync(); Assert.Empty(model.SectionError); Assert.Null(model.SelectedContainer!.ReceivedPerSecond);
        Assert.Equal(28.4, model.ContainerCpuPoints.Last().Value);
    });

    /// <summary>迟到 stats 不能填回重连后的容器或历史。</summary>
    [Fact]
    public Task DisconnectedStatsCannotWriteIntoTheNextConnection() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var late = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Handler = (command, token) =>
        {
            if (!command.StartsWith("LC_ALL=C docker stats ", StringComparison.Ordinal)) return data.HandleAsync(command, token);
            entered.TrySetResult(); return late.Task;
        };
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection); model.IsPaused = true; model.Section = "docker";
        var reading = model.RefreshDetailsCommand.ExecuteAsync(); await entered.Task; await model.StopAsync(); await reading;
        await model.StartAsync(data.CreateConnection()); model.IsPaused = true;
        late.TrySetResult(new(SshMonitorTestData.DockerStats, 0, false)); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Empty(model.Containers); Assert.Null(model.SelectedContainer); Assert.Empty(model.ContainerCpuPoints);
    });

    /// <summary>跟踪命令确实进入新 Shell，已有控制台的未完成输入保持不变。</summary>
    [Fact]
    public Task FollowingLogsOpensANewPtyAndPreservesTheExistingShell() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshMonitorTestData().CreateConnection();
        await using var workspace = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
        await workspace.InitializeAsync(); var initial = workspace.ActiveConsole;
        await workspace.DockerCommands.InitializeAsync(); workspace.DockerCommands.Select("logs", workspace.DockerCommands.Containers[1]); workspace.DockerCommands.Follow = true;
        string preview = workspace.DockerCommands.Preview; workspace.IsDockerCommandsOpen = true;
        await workspace.DockerCommands.ExecuteCommand.ExecuteAsync(); await SshWorkspaceTests.UntilAsync(() => connection.Consoles.Count == 2 && connection.Consoles[1].Inputs.Count > 0);
        Assert.NotSame(initial, workspace.ActiveConsole); Assert.Empty(connection.Consoles[0].Inputs);
        Assert.Equal(preview + "\r", Assert.Single(connection.Consoles[1].Inputs)); Assert.False(workspace.IsDockerCommandsOpen); Assert.Empty(workspace.DockerCommands.Error);
    });
}
