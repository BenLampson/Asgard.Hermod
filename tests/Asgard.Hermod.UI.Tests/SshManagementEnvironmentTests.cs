using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Ssh;
using Asgard.Hermod.Ssh.UI.Services;
using Asgard.Hermod.Ssh.UI.ViewModels;
using Asgard.Hermod.Ssh.UI.Views;

namespace Asgard.Hermod.UI.Tests;

/// <summary>手动环境入口和短窗口、长输出布局回归；不连接真实远端。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SshManagementEnvironmentTests(WpfTestFixture fixture)
{
    /// <summary>打开、关闭、重新打开两个管理工具都不能发起环境探测。</summary>
    [Fact]
    public Task OpeningManagementToolsDoesNotLoadEnvironment() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = SshKubernetesTests.Connection();
        await using var model = SshWorkspaceTests.Create(new SshTestFactory { Handler = _ => Task.FromResult<ISshConnection>(connection) });
        await model.InitializeAsync(); model.Monitor.IsPaused = true;
        int requests = connection.Commands.Count;
        foreach (string section in new[] { "docker", "kubernetes", "docker", "kubernetes" })
        {
            await model.ToggleToolboxCommand.ExecuteAsync(section);
            model.CollapseToolboxCommand.Execute(null);
        }
        await model.ToggleDockerCommandsCommand.ExecuteAsync();
        await model.ToggleKubernetesCommand.ExecuteAsync();
        Assert.Equal(requests, connection.Commands.Count);
        Assert.False(model.DockerCommands.IsInitialized); Assert.False(model.Kubernetes.IsInitialized);
    });

    /// <summary>部分资源权限失败独立缓存，切换不重试，刷新才能看见远端变化。</summary>
    [Fact]
    public Task SnapshotErrorsAndChangesAreRetainedUntilManualRefresh() => fixture.RunOffscreenAsync(async () =>
    {
        var connection = SshKubernetesTests.Connection(); var handler = connection.Handler!; bool denied = true;
        connection.Handler = (command, token) => denied && command.Contains(" get services ", StringComparison.Ordinal)
            ? Task.FromResult(new SshCommandResult("private bearer token", 1, false)) : handler(command, token);
        using var model = new SshKubernetesViewModel(() => connection, () => "host", new Dialogs());
        await model.DetectCommand.ExecuteAsync();
        Assert.NotEmpty(model.Items); Assert.Empty(model.Error);
        model.Resource = "services"; Assert.Empty(model.Items); Assert.Equal(Text.Get("KubeQueryFailed"), model.Error);
        int requests = connection.Commands.Count; denied = false;
        model.Resource = "pods"; Assert.Empty(model.Error); Assert.NotEmpty(model.Items);
        model.Resource = "services"; Assert.NotEmpty(model.Error); Assert.Equal(requests, connection.Commands.Count);
        await model.DetectCommand.ExecuteAsync(); Assert.Empty(model.Error); Assert.NotEmpty(model.Items);
    });

    /// <summary>真实 XAML 在短窗口中滚动；输入框完整，长目标换行，长输出支持换行和双向滚动。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task ManagementViewsKeepSearchAndLongOutputAccessible(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        try
        {
            string longOutput = string.Join("\n", Enumerable.Range(1, 80).Select(index => $"line {index}: " + new string('x', 220)));
            var connection = SshKubernetesTests.Connection(); var handler = connection.Handler!;
            connection.Handler = (command, token) => command.Contains(" describe ", StringComparison.Ordinal)
                ? Task.FromResult(new SshCommandResult(longOutput, 0, false)) : handler(command, token);
            using var kube = new SshKubernetesViewModel(() => connection, () => new string('h', 240), new Dialogs());
            await kube.DetectCommand.ExecuteAsync(); kube.SelectedItem = kube.Items[0]; await kube.ActionCommand.ExecuteAsync("describe");
            var kubeView = new SshKubernetesView { DataContext = kube };
            await VerifyView(kubeView, "kubernetes", language, theme);
            var data = new SshMonitorTestData(); var dockerConnection = data.CreateConnection();
            dockerConnection.Handler = (command, token) => command.StartsWith("docker logs ", StringComparison.Ordinal)
                ? Task.FromResult(new SshCommandResult(longOutput, 0, false)) : data.HandleAsync(command, token);
            using var docker = new SshDockerCommandsViewModel(() => dockerConnection, () => new string('h', 240), new Dialogs(), () => true, (_, _) => Task.CompletedTask);
            await docker.DetectCommand.ExecuteAsync(); docker.SelectedContainer = docker.Containers.First(container => container.IsRunning);
            await docker.ActionCommand.ExecuteAsync("logs");
            await VerifyView(new SshDockerCommandsView { DataContext = docker }, "docker", language, theme);
        }
        finally { Text.SetApplicationCulture(null); themes.SetTheme(UiTheme.Light); }
    });

    private static async Task VerifyView(UserControl view, string name, string language, UiTheme theme)
    {
        await SshKubernetesTests.Layout(view, 620, 480);
        var page = Assert.IsType<ScrollViewer>(view.FindName("PageScroll"));
        Assert.True(page.ScrollableHeight > 200);
        var target = Assert.IsType<TextBlock>(view.FindName("ResultTargetText"));
        Assert.Equal(TextWrapping.Wrap, target.TextWrapping); Assert.True(target.ActualHeight > 20);
        foreach (string searchName in name == "docker" ? new[] { "OutputSearch" } : new[] { "OutputSearch", "ResourceSearch" })
        {
            var search = Assert.IsType<TextBox>(view.FindName(searchName));
            var parent = Assert.IsAssignableFrom<FrameworkElement>(search.Parent);
            Assert.True(search.ActualHeight >= search.MinHeight);
            Assert.True(search.TranslatePoint(new(), parent).Y + search.ActualHeight <= parent.ActualHeight + 1);
        }
        var output = Assert.IsType<TextBox>(view.FindName("ResultOutput"));
        Assert.True(output.ActualHeight >= 180); Assert.Equal(TextWrapping.Wrap, output.TextWrapping);
        output.ScrollToEnd(); await WpfTestFixture.DrainDispatcherAsync(); Assert.True(output.VerticalOffset > 0);
        Assert.True(output.VerticalOffset + output.ViewportHeight >= output.ExtentHeight - 1);
        var wrap = Assert.IsType<CheckBox>(view.FindName("WrapOutput")); wrap.IsChecked = false;
        await SshKubernetesTests.Layout(view, 620, 480);
        Assert.Equal(TextWrapping.NoWrap, output.TextWrapping);
        output.ScrollToHorizontalOffset(10000); await WpfTestFixture.DrainDispatcherAsync(); Assert.True(output.HorizontalOffset > 0);
        wrap.IsChecked = true; page.ScrollToEnd(); await WpfTestFixture.DrainDispatcherAsync();
        SshKubernetesTests.Capture(view, $"management-{name}-long-{language}-{theme}");
    }

    private sealed class Dialogs : ISshWorkspaceDialogs
    {
        public Task<bool> ConfirmCloseAsync(string instanceName) => Task.FromResult(false);
    }
}
