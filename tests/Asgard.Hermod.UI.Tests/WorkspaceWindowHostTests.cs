using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过实际工作区导航验证独立承载，不创建无关测试窗口替代产品路径。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceWindowHostTests(WpfTestFixture fixture)
{
    /// <summary>实际实例打开路径复用窗口；主窗体隐藏后终端和独立任务栏入口仍保留。</summary>
    [Fact]
    public Task Navigation_OpensIndependentWindowAndPreservesSession() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        environment.Shell.Workspace.OpenInstance(environment.Local);
        var window = Assert.Single(host.Windows);
        var page = window.Page;
        Assert.True(window.IsVisible);
        Assert.True(window.ShowInTaskbar);
        Assert.Null(window.Owner);
        Assert.Same(environment.Shell.Workspace.Home, environment.Shell.Workspace.DisplayedPage);
        environment.Shell.Workspace.OpenInstance(environment.Local);
        Assert.Same(window, Assert.Single(host.Windows));
        Assert.Equal(1, environment.Factory.OpenAttempts);
        window.Close();
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(page.IsClosed);
        Assert.Empty(host.Windows);
        Assert.False(environment.Shell.Workspace.Home.IsClosed);
    });

    /// <summary>多个真实终端窗口分别关闭；最后一个窗口关闭不会释放外壳。</summary>
    [Fact]
    public Task LastWorkWindowClosing_LeavesShellAndHomeAlive() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        environment.Shell.Workspace.OpenLocalTerminal();
        environment.Shell.Workspace.OpenLocalTerminal();
        var windows = host.Windows.ToArray();
        Assert.Equal(2, windows.Length);
        windows[0].Close();
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(windows[1].IsVisible);
        Assert.False(windows[1].Page.IsClosed);
        windows[1].Close();
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.Empty(host.Windows);
        environment.Shell.Workspace.OpenLocalTerminal();
        Assert.Single(host.Windows);
        await environment.Shell.Workspace.DisposeAsync();
        await WpfTestFixture.DrainDispatcherAsync();
    });
}
