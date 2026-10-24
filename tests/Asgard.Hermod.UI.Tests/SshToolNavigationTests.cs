using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>Docker 与 kubectl 是独立管理入口，资源页面不能改变实例监测栏目。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshToolNavigationTests(WpfTestFixture fixture)
{
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
        Assert.True(Assert.IsType<Button>(view.FindName("KubernetesAction")).IsEnabled);
        await model.ToggleDockerCommandsCommand.ExecuteAsync(); await model.ShowDockerResourcesCommand.ExecuteAsync();
        await model.DockerMonitor.RefreshDetailsCommand.ExecuteAsync();
        Assert.True(model.IsDockerCommandsOpen); Assert.True(model.IsDockerResourcesOpen); Assert.Equal("network", model.Monitor.Section);
        Assert.Equal("docker", model.DockerMonitor.Section); Assert.NotEmpty(model.DockerMonitor.Containers);
        model.IsDockerResourcesOpen = false; Assert.True(model.DockerMonitor.IsPaused); Assert.True(model.IsDockerCommandsOpen);
        await model.ToggleKubernetesCommand.ExecuteAsync(); Assert.True(model.IsKubernetesOpen); Assert.False(model.IsDockerCommandsOpen);
        Assert.True(model.Kubernetes.IsInstalled); Assert.Equal("network", model.Monitor.Section);
        model.ToggleMonitorCommand.Execute(null); Assert.False(model.IsKubernetesOpen); Assert.True(model.IsMonitorOpen);
        Assert.Empty(bindings.Messages);
    });
}
