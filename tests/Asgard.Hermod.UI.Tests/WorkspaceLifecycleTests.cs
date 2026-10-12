using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证页签缓存、解绑顺序、连接释放与重试边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceLifecycleTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用真实 STA 资源和控件环境。</summary>
    public WorkspaceLifecycleTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>切换页签复用原视图，不会释放还在运行的会话。</summary>
    [Fact]
    public Task SwitchingTabs_ReusesTerminalViewAndKeepsSessionAlive() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var workspace = environment.Shell.Workspace;
        workspace.OpenInstance(environment.Local);
        var page = Assert.IsType<TerminalWorkspaceViewModel>(workspace.SelectedTab);
        var session = Assert.Single(environment.Factory.Sessions);
        using var host = environment.CreateContentHost();
        await WpfTestFixture.DrainDispatcherAsync();
        var originalView = Assert.IsType<TerminalWorkspaceView>(host.Content);
        var control = (TerminalControl)originalView.FindName("TerminalView");
        Assert.Same(session.Terminal, control.Session);

        workspace.SelectedTab = workspace.Tabs[0];
        await WpfTestFixture.DrainDispatcherAsync();
        workspace.SelectedTab = page;
        await WpfTestFixture.DrainDispatcherAsync();

        Assert.Same(originalView, host.Content);
        Assert.Same(session.Terminal, control.Session);
        Assert.Equal(0, session.DisposeCalls);
        session.Terminal.FeedOutput("still running"u8);
        Assert.Contains("still running", session.Terminal.GetVisibleText());
    });

    /// <summary>关闭开始即同步解绑控件，等待连接清理时也不会重复释放。</summary>
    [Fact]
    public Task ClosingTab_UnbindsBeforeAsyncDisposalAndDisposesOnce() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        environment.Factory.HoldDisposal = true;
        var workspace = environment.Shell.Workspace;
        workspace.OpenInstance(environment.Local);
        var page = Assert.IsType<TerminalWorkspaceViewModel>(workspace.SelectedTab);
        var session = Assert.Single(environment.Factory.Sessions);
        using var host = environment.CreateContentHost();
        await WpfTestFixture.DrainDispatcherAsync();
        var view = Assert.IsType<TerminalWorkspaceView>(host.Content);
        var control = (TerminalControl)view.FindName("TerminalView");
        Assert.Same(session.Terminal, control.Session);
        bool detachedBeforeConnectionCleanup = false;
        session.OnDisposalStarting = () => detachedBeforeConnectionCleanup =
            page.Session is null && control.Session is null && view.DataContext is null;

        Task closing = workspace.CloseTabAsync(page);
        Task secondClose = page.CloseAsync();

        Assert.True(detachedBeforeConnectionCleanup);
        Assert.True(page.IsClosed);
        Assert.Null(page.Session);
        Assert.Null(control.Session);
        Assert.DoesNotContain(page, workspace.Tabs);
        Assert.False(closing.IsCompleted);
        Assert.False(secondClose.IsCompleted);
        Assert.Same(secondClose, page.CloseAsync());
        Assert.Equal(1, session.DisposeCalls);

        session.ReleaseDisposal();
        await Task.WhenAll(closing, secondClose);
        Assert.True(session.IsDisposed);
        Assert.Equal(1, session.DisposeCalls);
    });

    /// <summary>已经打开的本地实例复用页签，关闭之后再次打开创建全新会话。</summary>
    [Fact]
    public Task ReopeningClosedLocalInstance_CreatesNewSession() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var workspace = environment.Shell.Workspace;
        workspace.OpenInstance(environment.Local);
        var originalPage = workspace.SelectedTab;
        var originalSession = Assert.Single(environment.Factory.Sessions);

        workspace.OpenInstance(environment.Local);
        Assert.Same(originalPage, workspace.SelectedTab);
        Assert.Equal(1, environment.Factory.OpenAttempts);

        await workspace.CloseTabAsync(originalPage);
        workspace.OpenInstance(environment.Local);

        Assert.NotSame(originalPage, workspace.SelectedTab);
        Assert.Equal(2, environment.Factory.OpenAttempts);
        var nextPage = Assert.IsType<TerminalWorkspaceViewModel>(workspace.SelectedTab);
        Assert.NotSame(originalSession.Terminal, nextPage.Session);
        Assert.True(originalSession.IsDisposed);
        Assert.Equal(1, originalSession.DisposeCalls);
    });

    /// <summary>应用退出同时启动清理并等待每个终端，期间拒绝继续打开页签。</summary>
    [Fact]
    public Task DisposingWorkspace_WaitsForEverySessionAndPreventsNewOpen() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        environment.Factory.HoldDisposal = true;
        var workspace = environment.Shell.Workspace;
        workspace.OpenLocalTerminal();
        workspace.OpenLocalTerminal();
        var sessions = environment.Factory.Sessions.ToArray();
        Assert.Equal(2, sessions.Length);

        Task closing = environment.Shell.DisposeAsync().AsTask();
        Assert.Empty(workspace.Tabs);
        Assert.All(sessions, session => Assert.Equal(1, session.DisposeCalls));
        Assert.False(closing.IsCompleted);
        Assert.Throws<ObjectDisposedException>(() => workspace.OpenLocalTerminal());

        sessions[0].ReleaseDisposal();
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.False(closing.IsCompleted);

        sessions[1].ReleaseDisposal();
        await closing;
        await workspace.DisposeAsync();
        Assert.All(sessions, session =>
        {
            Assert.True(session.IsDisposed);
            Assert.Equal(1, session.DisposeCalls);
        });
    });

    /// <summary>启动失败仍保留可重试页面，重试成功后清除错误。</summary>
    [Fact]
    public Task StartupFailure_CanRetryAndClearsError() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        environment.Factory.NextFailure = new IOException("启动测试失败");
        environment.Shell.Workspace.OpenInstance(environment.Local);
        var page = Assert.IsType<TerminalWorkspaceViewModel>(environment.Shell.Workspace.SelectedTab);

        Assert.Null(page.Session);
        Assert.Equal(Text.Get("FailedToStart"), page.Status);
        Assert.Equal(Text.Get("CannotStartTheTerminalCheckTheShellAndAccessPermissionsThenTryAgain"), page.Error);
        Assert.True(page.RetryCommand.CanExecute(null));
        Assert.Empty(environment.Factory.Sessions);

        await page.RetryCommand.ExecuteAsync();

        Assert.Null(page.RetryCommand.LastException);
        Assert.Null(page.Error);
        Assert.True(page.IsRunning);
        Assert.False(page.CanRetry);
        Assert.Equal(2, environment.Factory.OpenAttempts);
        Assert.Same(Assert.Single(environment.Factory.Sessions).Terminal, page.Session);
    });

    /// <summary>已退出会话在重试时先解绑并等待旧连接释放，再绑定新会话。</summary>
    [Fact]
    public Task RetryingExitedSession_WaitsForOldDisposalAndRebindsControl() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        environment.Factory.HoldDisposal = true;
        environment.Shell.Workspace.OpenInstance(environment.Local);
        var page = Assert.IsType<TerminalWorkspaceViewModel>(environment.Shell.Workspace.SelectedTab);
        var previous = Assert.Single(environment.Factory.Sessions);
        using var host = environment.CreateContentHost();
        await WpfTestFixture.DrainDispatcherAsync();
        var view = Assert.IsType<TerminalWorkspaceView>(host.Content);
        var control = (TerminalControl)view.FindName("TerminalView");

        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnStopped(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(page.IsRunning) && !page.IsRunning)
                stopped.TrySetResult();
        }
        page.PropertyChanged += OnStopped;
        try
        {
            previous.Complete(7);
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
        finally
        {
            page.PropertyChanged -= OnStopped;
        }
        Assert.Equal(Text.Format("Exited", 7), page.Status);

        Task retry = page.RetryCommand.ExecuteAsync();
        Assert.Null(page.Session);
        Assert.Null(control.Session);
        Assert.False(retry.IsCompleted);
        Assert.Equal(1, environment.Factory.OpenAttempts);

        previous.ReleaseDisposal();
        await retry;
        await WpfTestFixture.DrainDispatcherAsync();

        Assert.True(previous.IsDisposed);
        Assert.Equal(1, previous.DisposeCalls);
        Assert.Equal(2, environment.Factory.OpenAttempts);
        Assert.Same(environment.Factory.Sessions[1].Terminal, control.Session);
        Assert.Same(view, host.Content);
    });
}
