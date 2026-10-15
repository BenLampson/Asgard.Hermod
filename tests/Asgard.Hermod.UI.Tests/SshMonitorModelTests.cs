using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>离线验证结构化监测的筛选、失败保留、并发取消与日志边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshMonitorModelTests(WpfTestFixture fixture)
{
    /// <summary>首次 CPU 未知；表格按数字排序，搜索不进入远端命令。</summary>
    [Fact]
    public Task StructuredRowsSortAndFilterLocally() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection);
        Assert.Null(model.Snapshot!.CpuPercent); Assert.Null(model.History[0].Cpu);
        model.IsPaused = true; model.Section = "processes"; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Null(model.RefreshDetailsCommand.LastException);
        Assert.Equal(6, model.Processes.Count); Assert.Equal(2, model.RunningProcesses);
        Assert.Equal(1.8, model.Processes[0].CpuPercent, 5);
        int commands = connection.Commands.Count;
        model.Search = "WWW"; Assert.Equal("nginx", Assert.Single(model.ProcessView.Cast<SshMonitorProcessItem>()).Name);
        model.Search = "2201"; Assert.Equal("dotnet", Assert.Single(model.ProcessView.Cast<SshMonitorProcessItem>()).Name);
        Assert.Equal(commands, connection.Commands.Count);
        model.Search = "no-match"; Assert.True(model.ShowEmpty);
        model.ClearSearchCommand.Execute(null); Assert.False(model.ShowEmpty);
        Assert.DoesNotContain(connection.Commands, command => command.Contains("no-match", StringComparison.Ordinal));
    });

    /// <summary>采样失败保留最后成功读数，历史断点为空且错误不含秘密。</summary>
    [Fact]
    public Task FailedSampleKeepsSuccessfulSnapshotAndBreaksHistory() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData();
        await using var model = new SshMonitorViewModel(); await model.StartAsync(data.CreateConnection()); model.IsPaused = true;
        await model.RefreshDetailsCommand.ExecuteAsync(); var previous = model.Snapshot;
        data.FailSampling = true; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Same(previous, model.Snapshot); Assert.True(model.HasBanner);
        Assert.DoesNotContain("password", model.BannerText, StringComparison.OrdinalIgnoreCase);
        Assert.Null(model.History[^1].Cpu); Assert.Null(model.History[^1].Sent);
        Assert.True(model.History.Count <= 25);
    });

    /// <summary>容器与挂载点使用独立数值和稳定协议状态，筛选清理后可恢复。</summary>
    [Fact]
    public Task ContainerFiltersAndMountCapacityUseStructuredFields() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData();
        await using var model = new SshMonitorViewModel(); await model.StartAsync(data.CreateConnection()); model.IsPaused = true;
        model.Section = "docker"; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Null(model.RefreshDetailsCommand.LastException);
        Assert.Equal(5, model.ContainerCount); Assert.Equal(4, model.RunningContainers); Assert.Equal(1, model.UnhealthyContainers);
        model.ContainerState = "exited"; Assert.Single(model.ContainerView.Cast<object>());
        model.ClearSearchCommand.Execute(null); Assert.Equal(5, model.ContainerView.Cast<object>().Count());
        model.Section = "disks"; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Equal(3, model.PersistentCount); Assert.Equal(1, model.HighUsageCount);
        Assert.Equal("/data", model.MountView.Cast<SshMonitorMountItem>().First().Path);
        Assert.True(model.ShowMountWarning); Assert.Contains("/data", model.MountWarning, StringComparison.Ordinal);
    });

    /// <summary>Docker 未安装、权限不足与无容器必须保持不同状态。</summary>
    [Theory]
    [InlineData(42, SshContainerAvailability.NotInstalled)]
    [InlineData(43, SshContainerAvailability.AccessDenied)]
    [InlineData(0, SshContainerAvailability.Available)]
    public Task DockerEmptyAndUnavailableStatesAreDistinct(int exitCode, SshContainerAvailability expected) => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        connection.Handler = (command, token) => command == SshMonitorDetailReader.ContainerCommand
            ? Task.FromResult(new SshCommandResult("", exitCode, false)) : data.HandleAsync(command, token);
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection); model.IsPaused = true;
        model.Section = "docker"; await model.RefreshDetailsCommand.ExecuteAsync();
        Assert.Equal(expected, model.ContainerAvailability); Assert.True(model.ShowEmpty); Assert.Empty(model.Containers);
    });

    /// <summary>日志仅接受列表中的容器，关闭保留所选行；失败不展示 stderr 原文。</summary>
    [Fact]
    public Task ContainerLogsUseCanonicalIdAndHideFailures() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection); model.IsPaused = true;
        model.Section = "docker"; await model.RefreshDetailsCommand.ExecuteAsync();
        var item = model.Containers[0];
        await model.ReadLogsCommand.ExecuteAsync(item); Assert.True(model.IsLogOpen); Assert.Contains("GET /health 200", model.LogText);
        Assert.EndsWith(item.Id, connection.Commands[^1]);
        model.CloseLogsCommand.Execute(null); Assert.False(model.IsLogOpen); Assert.Same(item, model.SelectedContainer);
        connection.Handler = (command, token) => command.StartsWith("docker logs ", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult("secret credentials in stderr", 1, false)) : data.HandleAsync(command, token);
        await model.ReadLogsCommand.ExecuteAsync(item);
        Assert.Empty(model.LogText); Assert.Equal(Text.Get("SshMonitorLogFailed"), model.LogStatus);
        Assert.DoesNotContain("secret", model.LogStatus);
    });

    /// <summary>断线取消不合作的旧响应，重连后也不能填回上一台主机的进程。</summary>
    [Fact]
    public Task OldDetailsCannotOverwriteReconnectedHost() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocked = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Handler = (command, token) =>
        {
            if (command != SshMonitorDetailReader.ProcessCommand) return data.HandleAsync(command, token);
            arrived.TrySetResult(); return blocked.Task;
        };
        await using var model = new SshMonitorViewModel(); await model.StartAsync(connection); model.IsPaused = true;
        model.Section = "processes"; var reading = model.RefreshDetailsCommand.ExecuteAsync(); await arrived.Task;
        await model.StopAsync(); await reading; await model.StartAsync(data.CreateConnection()); model.IsPaused = true;
        blocked.SetResult(new(SshMonitorTestData.Processes, 0, false)); await WpfTestFixture.DrainDispatcherAsync();
        Assert.Empty(model.Processes); Assert.False(model.IsLogOpen);
    });
}
