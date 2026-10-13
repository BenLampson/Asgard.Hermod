using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证所有页签可关闭、空工作区可再次打开实例以及重启不恢复页签。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceTabClosingTests(WpfTestFixture fixture)
{
    /// <summary>首页和标签页均显示关闭按钮，关闭最后一页清空内容并允许再次打开实例。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ClosingLastNavigationTab_ClearsContentAndAllowsNewOpen(bool isTag) => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var workspace = environment.Shell.Workspace;
        WorkspacePageViewModel page = workspace.Home;
        if (isTag)
        {
            await workspace.CloseTabAsync(page);
            page = new EmptyWorkspaceViewModel("tag:production", "Production", "tag");
            workspace.Tabs.Add(page);
            workspace.SelectedTab = page;
        }
        using var host = environment.CreateContentHost();
        var strip = new WorkspaceTabStrip { DataContext = workspace };
        var panel = new StackPanel();
        panel.Children.Add(strip);
        panel.Children.Add(host);
        var window = new Window { Content = panel, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            var close = Assert.Single(WindowTestVisuals.Descendants<Button>(strip),
                button => ReferenceEquals(button.Command, workspace.CloseTabCommand));
            Assert.True(page.CanClose);
            Assert.True(close.IsVisible);
            Assert.True(close.Focusable);
            Assert.Same(page, close.CommandParameter);
            Assert.NotNull(host.Content);

            await workspace.CloseTabCommand.ExecuteAsync(close.CommandParameter);
            await WpfTestFixture.DrainDispatcherAsync();

            Assert.Null(workspace.CloseTabCommand.LastException);
            Assert.Empty(workspace.Tabs);
            Assert.Null(workspace.SelectedTab);
            Assert.Null(host.Content);
            Assert.True(page.IsClosed);
            Assert.False(page.IsSelected);

            workspace.OpenInstance(environment.Local);
            var terminal = Assert.IsType<TerminalWorkspaceViewModel>(workspace.SelectedTab);
            Assert.Same(terminal, Assert.Single(workspace.Tabs));
            await workspace.CloseTabAsync(terminal);
            Assert.Null(workspace.SelectedTab);
            Assert.True(Assert.Single(environment.Factory.Sessions).IsDisposed);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>关闭非活动页保留选中页，关闭首个活动页选择右邻页。</summary>
    [Fact]
    public Task ClosingTabs_KeepsOrSelectsAdjacentPage() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var workspace = environment.Shell.Workspace;
        var instance = environment.Shell.Sidebar.AllInstances.First(item => !item.IsLocal);
        workspace.OpenInstance(instance);
        var service = Assert.IsType<ServiceDetailViewModel>(workspace.SelectedTab);
        var tag = new EmptyWorkspaceViewModel("tag:production", "Production", "tag");
        workspace.Tabs.Add(tag);

        await workspace.CloseTabAsync(tag);
        Assert.Same(service, workspace.SelectedTab);
        workspace.SelectedTab = workspace.Home;
        await workspace.CloseTabAsync(workspace.Home);
        Assert.Same(service, workspace.SelectedTab);
        await workspace.CloseTabAsync(service);
        Assert.Empty(workspace.Tabs);
        Assert.Null(workspace.SelectedTab);
        Assert.DoesNotContain(environment.Shell.Sidebar.AllInstances, item => item.IsSelected);
    });

    /// <summary>新建工作区始终只有首页，不恢复前一工作区的实例和标签页签。</summary>
    [Fact]
    public Task Restart_OpensOnlyHomeRegardlessOfPreviousTabs() => fixture.RunAsync(async () =>
    {
        await using var previous = new ShellTestEnvironment();
        previous.Shell.Workspace.OpenInstance(previous.Local);
        previous.Shell.Workspace.Tabs.Add(new EmptyWorkspaceViewModel("tag:production", "Production", "tag"));
        await previous.Shell.DisposeAsync();

        await using var restarted = new ShellTestEnvironment();
        await restarted.Shell.InitializeAsync();
        var workspace = restarted.Shell.Workspace;
        Assert.Same(workspace.Home, Assert.Single(workspace.Tabs));
        Assert.Same(workspace.Home, workspace.SelectedTab);
        Assert.Equal(0, restarted.Factory.OpenAttempts);
    });
}
