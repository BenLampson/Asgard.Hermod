using WorkspaceTabStrip = Asgard.Hermod.UI.Views.Shell.WorkspaceTabStrip;

namespace Asgard.Hermod.UI.Tests;

/// <summary>首页常驻，其他页签可关闭；关闭最后一个工作页返回首页，重启不恢复会话。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceTabClosingTests(WpfTestFixture fixture)
{
    /// <summary>小房子不显示关闭按钮，命令与直接业务入口都不能关闭首页。</summary>
    [Fact]
    public Task HomeCannotBeClosedThroughAnyUserEntry() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var workspace = environment.Shell.Workspace;
        using var host = environment.CreateContentHost();
        var strip = new WorkspaceTabStrip { DataContext = workspace };
        var panel = new StackPanel(); panel.Children.Add(strip); panel.Children.Add(host);
        var window = new Window { Content = panel, ShowInTaskbar = false, ShowActivated = false, Opacity = 0 };
        try
        {
            window.Show(); await WpfTestFixture.DrainDispatcherAsync();
            var close = Assert.Single(WindowTestVisuals.Descendants<Button>(strip), button => ReferenceEquals(button.Command, workspace.CloseTabCommand));
            Assert.False(workspace.Home.CanClose); Assert.False(close.IsVisible); Assert.False(close.IsEnabled);
            Assert.False(workspace.CloseTabCommand.CanExecute(workspace.Home));
            var originalView = host.Content;
            await workspace.CloseTabCommand.ExecuteAsync(workspace.Home);
            await workspace.CloseTabAsync(workspace.Home);
            Assert.Same(workspace.Home, Assert.Single(workspace.Tabs)); Assert.Same(workspace.Home, workspace.SelectedTab);
            Assert.Same(originalView, host.Content); Assert.False(workspace.Home.IsClosed);
        }
        finally { window.Close(); }
    });

    /// <summary>标签和终端仍可关闭，最后一个工作页关闭后选择首页并允许再次打开实例。</summary>
    [Fact]
    public Task ClosingLastWorkTabReturnsHomeAndAllowsNewOpen() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment(); var workspace = environment.Shell.Workspace;
        var tag = new EmptyWorkspaceViewModel("tag:production", "Production", "tag");
        workspace.Tabs.Add(tag); workspace.SelectedTab = tag;
        Assert.True(workspace.CloseTabCommand.CanExecute(tag));
        await workspace.CloseTabCommand.ExecuteAsync(tag);
        Assert.Same(workspace.Home, Assert.Single(workspace.Tabs)); Assert.Same(workspace.Home, workspace.SelectedTab);
        Assert.True(tag.IsClosed); Assert.False(workspace.CloseTabCommand.CanExecute(tag));
        workspace.OpenInstance(environment.Local); var terminal = Assert.IsType<TerminalWorkspaceViewModel>(workspace.SelectedTab);
        Assert.True(workspace.CloseTabCommand.CanExecute(terminal)); await workspace.CloseTabCommand.ExecuteAsync(terminal);
        Assert.Same(workspace.Home, Assert.Single(workspace.Tabs)); Assert.Same(workspace.Home, workspace.SelectedTab);
        Assert.True(Assert.Single(environment.Factory.Sessions).IsDisposed); Assert.False(workspace.Home.IsClosed);
    });

    /// <summary>关闭非活动页保留选中页，首页选中时忽略关闭，关闭服务页后仍保留首页。</summary>
    [Fact]
    public Task ClosingTabsKeepsSelectionAndPersistentHome() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment(); var workspace = environment.Shell.Workspace;
        workspace.OpenInstance(environment.Shell.Sidebar.AllInstances.First(item => !item.IsLocal));
        var service = Assert.IsType<ServiceDetailViewModel>(workspace.SelectedTab);
        var tag = new EmptyWorkspaceViewModel("tag:production", "Production", "tag"); workspace.Tabs.Add(tag);
        await workspace.CloseTabAsync(tag); Assert.Same(service, workspace.SelectedTab);
        workspace.SelectedTab = workspace.Home; await workspace.CloseTabAsync(workspace.Home);
        Assert.Same(workspace.Home, workspace.SelectedTab); Assert.False(workspace.Home.IsClosed);
        await workspace.CloseTabAsync(service); Assert.Same(workspace.Home, Assert.Single(workspace.Tabs));
        Assert.Same(workspace.Home, workspace.SelectedTab);
        Assert.DoesNotContain(environment.Shell.Sidebar.AllInstances, item => item.IsSelected);
    });

    /// <summary>新建工作区始终只有首页，不恢复前一工作区的实例和标签页签。</summary>
    [Fact]
    public Task RestartOpensOnlyHomeRegardlessOfPreviousTabs() => fixture.RunAsync(async () =>
    {
        await using var previous = new ShellTestEnvironment();
        previous.Shell.Workspace.OpenInstance(previous.Local);
        previous.Shell.Workspace.Tabs.Add(new EmptyWorkspaceViewModel("tag:production", "Production", "tag"));
        await previous.Shell.DisposeAsync();
        Assert.True(previous.Shell.Workspace.Home.IsClosed);
        await using var restarted = new ShellTestEnvironment(); await restarted.Shell.InitializeAsync();
        var workspace = restarted.Shell.Workspace;
        Assert.Same(workspace.Home, Assert.Single(workspace.Tabs)); Assert.Same(workspace.Home, workspace.SelectedTab);
        Assert.False(workspace.Home.CanClose); Assert.Equal(0, restarted.Factory.OpenAttempts);
    });
}
