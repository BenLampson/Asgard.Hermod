using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>Docker 与 kubectl 是独立管理入口，资源页面不能改变实例监测栏目。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshToolNavigationTests(WpfTestFixture fixture)
{
    /// <summary>主区域切换到资源页时选择同一容器并启动采样，离开后暂停。</summary>
    [Fact]
    public Task DockerResourcePageTracksSelectionAndPausesWhenLeaving() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = new SshMonitorTestData().CreateConnection();
        await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
        await model.InitializeAsync(); model.Monitor.IsPaused = true;
        await model.ToggleDockerCommandsCommand.ExecuteAsync(); await model.DockerCommands.DetectCommand.ExecuteAsync();
        var target = model.DockerCommands.Containers.First(container => container.Name == "hermod-api");
        model.DockerCommands.SelectedContainer = target; model.DockerCommands.ShowResourcesCommand.Execute(null);
        await model.DockerMonitor.RefreshDetailsCommand.ExecuteAsync();
        await SshWorkspaceTests.UntilAsync(() => model.DockerMonitor.SelectedContainer?.Id == target.Id);
        Assert.True(model.DockerCommands.IsResourceTool); Assert.True(model.IsDockerResourcesOpen);
        Assert.NotNull(model.DockerMonitor.SelectedContainer!.CpuPercent); Assert.False(model.DockerCommands.IsOutputOpen);
        model.DockerCommands.ShowDetailsCommand.Execute(null);
        Assert.True(model.DockerCommands.IsDetailsTool); Assert.False(model.IsDockerResourcesOpen); Assert.True(model.DockerMonitor.IsPaused);
        model.DockerCommands.ShowResourcesCommand.Execute(null);
        await SshWorkspaceTests.UntilAsync(() => model.IsDockerResourcesOpen && !model.DockerMonitor.IsPaused);
        Assert.Equal(target.Id, model.DockerCommands.SelectedContainer!.Id);
    });

    /// <summary>刷新尚未结束时切换页面，只在最终仍停留容器页时恢复采样。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task NavigationDuringResourceRefreshRespectsFinalPage(bool returnToContainers) => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection();
        await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
        await model.InitializeAsync(); model.Monitor.IsPaused = true;
        await model.ToggleDockerCommandsCommand.ExecuteAsync(); await model.DockerCommands.DetectCommand.ExecuteAsync();
        await SshWorkspaceTests.UntilAsync(() => model.IsDockerResourcesOpen && !model.ShowDockerResourcesCommand.IsRunning && !model.DockerMonitor.RefreshDetailsCommand.IsRunning);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool block = true;
        connection.Handler = async (command, token) =>
        {
            if (block && command.Contains("__CPU__", StringComparison.Ordinal))
            {
                block = false; entered.TrySetResult(); await release.Task.WaitAsync(token);
            }
            return await data.HandleAsync(command, token);
        };
        var refresh = model.ShowDockerResourcesCommand.ExecuteAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            model.DockerCommands.SelectToolCommand.Execute("images"); Assert.False(model.IsDockerResourcesOpen);
            model.DockerCommands.SelectToolCommand.Execute("containers");
            if (!returnToContainers) model.DockerCommands.SelectToolCommand.Execute("images");
        }
        finally { release.TrySetResult(); }
        await refresh;
        if (returnToContainers) await SshWorkspaceTests.UntilAsync(() => model.IsDockerResourcesOpen && !model.DockerMonitor.IsPaused);
        else { Assert.False(model.IsDockerResourcesOpen); Assert.True(model.DockerMonitor.IsPaused); }
    });

    [Fact]
    public Task DockerResourcesStayInsideManagementAndKubectlUsesSameConnection() => fixture.RunOffscreenAsync(async () =>
    {
        using var bindings = new WpfBindingTrace();
        var connection = SshKubernetesTests.Connection(); var kubectl = connection.Handler!; var data = new SshMonitorTestData();
        connection.Handler = (command, token) => command.Contains("kubectl", StringComparison.Ordinal) ? kubectl(command, token) : data.HandleAsync(command, token);
        await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
        await model.InitializeAsync(); model.Monitor.IsPaused = true; model.Monitor.Section = "network";
        using var view = new SshWorkspaceView { DataContext = model };
        await SshKubernetesTests.Layout(view, 1440, 900);
        var main = Assert.Single(WindowTestVisuals.Descendants<SshMonitorView>(view), monitor => !monitor.DockerOnly);
        var sections = Assert.IsType<SectionSelector>(main.FindName("MonitorSections"));
        Assert.Equal(new[] { "overview", "processes", "network", "disks" }, sections.Items.Cast<ListBoxItem>().Select(item => item.Tag));
        Assert.True(Assert.IsType<RadioButton>(view.FindName("KubernetesRailAction")).IsEnabled);
        await model.ToggleDockerCommandsCommand.ExecuteAsync(); await model.DockerCommands.DetectCommand.ExecuteAsync(); await model.ShowDockerResourcesCommand.ExecuteAsync();
        await model.DockerMonitor.RefreshDetailsCommand.ExecuteAsync();
        Assert.True(model.IsDockerCommandsOpen); Assert.True(model.IsDockerResourcesOpen); Assert.Equal("network", model.Monitor.Section);
        Assert.Equal("docker", model.DockerMonitor.Section); Assert.NotEmpty(model.DockerMonitor.Containers);
        model.IsDockerResourcesOpen = false; Assert.True(model.DockerMonitor.IsPaused); Assert.True(model.IsDockerCommandsOpen);
        await model.ToggleKubernetesCommand.ExecuteAsync(); Assert.True(model.IsKubernetesOpen); Assert.False(model.IsDockerCommandsOpen);
        Assert.False(model.Kubernetes.IsInstalled); Assert.DoesNotContain(connection.Commands, command => command.Contains("kubectl", StringComparison.Ordinal));
        await model.Kubernetes.DetectCommand.ExecuteAsync();
        Assert.True(model.Kubernetes.IsInstalled); Assert.Equal("network", model.Monitor.Section);
        model.ToggleMonitorCommand.Execute(null); Assert.True(model.IsKubernetesOpen); Assert.True(model.IsTerminalSection); Assert.True(model.IsMonitorOpen);
        Assert.True(bindings.Messages.Count == 0, string.Join(Environment.NewLine, bindings.Messages));
    });
}
