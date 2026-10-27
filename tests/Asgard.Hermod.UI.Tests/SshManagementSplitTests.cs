using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>基础设施管理与终端同时使用、手动加载及迟到结果的离屏回归。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshManagementSplitTests(WpfTestFixture fixture)
{
    /// <summary>中英文与主题均使用真实 XAML，宽屏并排、窄屏上下，拖动后的尺寸按工具保留。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ToolsKeepTerminalVisibleAndRememberIndependentSizes(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            var connection = SshKubernetesTests.Connection(); var kubectl = connection.Handler!; var docker = new SshMonitorTestData();
            connection.Handler = (command, token) => command.Contains("kubectl", StringComparison.Ordinal) ? kubectl(command, token) : docker.HandleAsync(command, token);
            await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
            await model.InitializeAsync(); model.Monitor.IsPaused = true;
            connection.Output("tester@target:~$ kubectl get pods -n app\r\nweb-1   1/1   Running\r\ntester@target:~$ ");
            await SshWorkspaceTests.UntilAsync(() => model.Session!.GetAllText().Contains("Running", StringComparison.Ordinal));
            using var bindings = new WpfBindingTrace(); using var view = new SshWorkspaceView { DataContext = model };
            var console = model.ActiveConsole; var session = model.Session;
            foreach (string section in new[] { "docker", "kubernetes" })
            {
                await model.ToggleToolboxCommand.ExecuteAsync(section);
                Assert.True(model.IsManagementSection); Assert.True(model.IsTerminalSection);
                await SshKubernetesTests.Layout(view, 1440, 1000);
                var grid = Assert.IsType<Grid>(view.FindName("ToolboxGrid"));
                var panel = Assert.IsType<Border>(view.FindName("ToolboxPanel"));
                var terminal = Assert.IsType<Grid>(view.FindName("TerminalSurface"));
                Assert.True(terminal.ActualWidth > 400); Assert.True(panel.ActualWidth > 400);
                Assert.InRange(Math.Abs(panel.ActualWidth - (grid.ActualWidth - 60) / 2), 0, 2);
                var panelAt = panel.TranslatePoint(new(), view); var terminalAt = terminal.TranslatePoint(new(), view);
                Assert.True(panelAt.X + panel.ActualWidth <= terminalAt.X);
                var column = Assert.IsType<ColumnDefinition>(view.FindName("ToolboxColumn")); column.Width = new(480);
                await SshKubernetesTests.Layout(view, 1440, 1000);
                model.CollapseToolboxCommand.Execute(null); await SshKubernetesTests.Layout(view, 1440, 1000);
                await model.ToggleToolboxCommand.ExecuteAsync(section); await SshKubernetesTests.Layout(view, 1440, 1000);
                Assert.Equal(480, panel.ActualWidth);
                var divider = Assert.IsType<GridSplitter>(view.FindName("ToolboxSplitter"));
                divider.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
                await SshKubernetesTests.Layout(view, 1440, 1000);
                Assert.InRange(Math.Abs(panel.ActualWidth - (grid.ActualWidth - 60) / 2), 0, 2);
                if (section == "docker")
                {
                    if (!model.DockerCommands.IsInitialized) await model.DockerCommands.DetectCommand.ExecuteAsync();
                    Assert.True(model.DockerCommands.IsInitialized); Assert.NotEmpty(model.DockerCommands.Containers);
                    model.DockerCommands.SelectedContainer = model.DockerCommands.Containers.First(c => c.IsRunning);
                    await SshKubernetesTests.Layout(view, 1440, 1000);
                    SshKubernetesTests.Capture(view, $"management-docker-resources-{language}-{theme}");
                    await model.DockerCommands.ActionCommand.ExecuteAsync("logs");
                }
                else
                {
                    if (!model.Kubernetes.IsInitialized) await model.Kubernetes.DetectCommand.ExecuteAsync();
                    Assert.True(model.Kubernetes.IsInitialized); Assert.NotEmpty(model.Kubernetes.Items);
                    model.Kubernetes.SelectedItem = model.Kubernetes.Items[0]; await model.Kubernetes.ActionCommand.ExecuteAsync("logs");
                }
                await SshKubernetesTests.Layout(view, 1440, 1000);
                SshKubernetesTests.Capture(view, $"management-{section}-{language}-{theme}-wide");
                await SshKubernetesTests.Layout(view, 820, 800);
                var splitter = Assert.IsType<GridSplitter>(view.FindName("ToolboxSplitter"));
                Assert.Equal(System.Windows.Controls.GridResizeDirection.Rows, splitter.ResizeDirection);
                panelAt = panel.TranslatePoint(new(), view); terminalAt = terminal.TranslatePoint(new(), view);
                Assert.True(panel.ActualHeight > 200); Assert.True(terminal.ActualHeight > 100);
                Assert.True(panelAt.Y + panel.ActualHeight <= terminalAt.Y);
                var row = Assert.IsType<RowDefinition>(view.FindName("ToolboxRow")); row.Height = new(350);
                await SshKubernetesTests.Layout(view, 820, 800);
                model.CollapseToolboxCommand.Execute(null); await SshKubernetesTests.Layout(view, 820, 800);
                await model.ToggleToolboxCommand.ExecuteAsync(section); await SshKubernetesTests.Layout(view, 820, 800);
                Assert.Equal(350, panel.ActualHeight);
                SshKubernetesTests.Capture(view, $"management-{section}-{language}-{theme}-stacked");
                Assert.Same(console, model.ActiveConsole); Assert.Same(session, model.Session); Assert.Contains("Running", session!.GetAllText()); Assert.All(connection.Consoles, console => Assert.Empty(console.Inputs));
                model.CollapseToolboxCommand.Execute(null);
            }
            Assert.True(bindings.Messages.Count == 0, string.Join(Environment.NewLine, bindings.Messages));
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>一次手动加载所有筛选所需快照，切换上下文、命名空间与类型零请求。</summary>
    [Fact]
    public Task KubernetesSelectionsUseSnapshotUntilExplicitRefresh() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = SshKubernetesTests.Connection(); using var model = new SshKubernetesViewModel(() => connection, () => "host", new Dialogs());
        Assert.Empty(connection.Commands); Assert.False(model.IsInitialized);
        await model.DetectCommand.ExecuteAsync();
        Assert.True(model.IsInitialized); Assert.Equal("production", model.Context); Assert.Equal("app", model.Namespace);
        Assert.NotEmpty(model.Namespaces); Assert.NotEmpty(model.Items);
        int requests = connection.Commands.Count;
        model.Namespace = "missing"; Assert.Empty(model.Items);
        model.Namespace = "app"; model.Resource = "deployments"; Assert.Equal("web", Assert.Single(model.Items).Name);
        model.Context = "staging"; Assert.Equal("app", model.Namespace); Assert.Single(model.Items);
        model.Resource = "namespaces"; Assert.Single(model.Items);
        Assert.Equal(requests, connection.Commands.Count);
        Assert.Equal(2, connection.Commands.Count(command => command.EndsWith("get namespaces -o json", StringComparison.Ordinal)));
        await model.DetectCommand.ExecuteAsync();
        Assert.True(connection.Commands.Count > requests); Assert.Equal("staging", model.Context);
        Assert.All(connection.Consoles, console => Assert.Empty(console.Inputs));
    });

    /// <summary>无命名空间列表权限仍缓存可读取资源；更改筛选不能重试网络或显示原始失败响应。</summary>
    [Fact]
    public Task NamespaceListFailurePreservesKnownScopeAndSafeRecovery() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = SshKubernetesTests.Connection(); var handler = connection.Handler!;
        connection.Handler = (command, token) => command.EndsWith("get namespaces -o json", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult("private bearer token", 1, false)) : handler(command, token);
        using var model = new SshKubernetesViewModel(() => connection, () => "host", new Dialogs());
        await model.InitializeAsync(); Assert.True(model.IsInitialized); Assert.NotEmpty(model.Items); Assert.NotEmpty(model.NamespaceWarning);
        Assert.DoesNotContain("private", model.NamespaceWarning); Assert.Empty(model.Error);
        int requests = connection.Commands.Count;
        model.Namespace = "known-namespace"; Assert.Empty(model.Items);
        model.Namespace = "app"; Assert.NotEmpty(model.Items); Assert.Equal(requests, connection.Commands.Count);
    });

    /// <summary>旧连接的初始化完成不能清除新连接的忙碌状态或写入旧环境。</summary>
    [Fact]
    public Task LateInitializationCannotReplaceNewConnectionState() => fixture.RunOffscreenAsync(async () =>
    {
        var old = SshKubernetesTests.Connection(); var current = old;
        var oldProbe = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        old.Handler = (_, _) => oldProbe.Task;
        using var model = new SshKubernetesViewModel(() => current, () => "host", new Dialogs());
        var initial = model.InitializeAsync(); model.ConnectionChanged(); current = SshKubernetesTests.Connection(); var handler = current.Handler!;
        var newProbe = new TaskCompletionSource<SshCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        current.Handler = (command, token) => command == SshKubernetesCommands.Probe ? newProbe.Task : handler(command, token);
        var replacement = model.InitializeAsync();
        oldProbe.SetResult(new("{}", 0, false)); await initial; Assert.True(model.IsBusy);
        newProbe.SetResult(new("""{"clientVersion":{"gitVersion":"v1.36.1"}}""", 0, false)); await replacement;
        Assert.True(model.IsInitialized); Assert.Contains("v1.36.1", model.EnvironmentSummary); Assert.NotEmpty(model.Items);
    });

    /// <summary>检测成功但列表读取失败仍可一键恢复；状态筛选撤销隐藏容器的动作权限。</summary>
    [Fact]
    public Task DockerRetriesIncompleteInitializationAndClearsFilteredTargets() => fixture.RunOffscreenAsync(async () =>
    {
        var data = new SshMonitorTestData(); var connection = data.CreateConnection(); bool fail = true;
        connection.Handler = (command, token) => command == SshMonitorDetailReader.ContainerCommand && fail
            ? Task.FromResult(new SshCommandResult("private daemon response", 1, false)) : data.HandleAsync(command, token);
        using var model = new SshDockerCommandsViewModel(() => connection, () => "host", new Dialogs(), () => true, (_, _) => Task.CompletedTask);
        await model.InitializeAsync(); Assert.False(model.IsInitialized); Assert.DoesNotContain("private", model.Error);
        fail = false; await model.InitializeAsync(); Assert.True(model.IsInitialized); Assert.NotEmpty(model.Containers); Assert.Empty(model.Error);
        model.SelectedContainer = model.Containers.First(c => c.IsRunning); model.ContainerState = "exited";
        Assert.Null(model.SelectedContainer); Assert.False(model.ActionCommand.CanExecute("restart"));
        model.Select("logs", model.Containers.First(c => c.IsRunning)); Assert.Equal("", model.ContainerState);
        Assert.NotNull(model.SelectedContainer); await model.ActionCommand.ExecuteAsync("logs"); Assert.NotEmpty(model.Output);
        model.ConnectionChanged(); Assert.Empty(model.Output); Assert.Empty(model.ResultTarget); Assert.False(model.IsInitialized);
    });

    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false);
    }
}
