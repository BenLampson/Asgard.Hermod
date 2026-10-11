namespace Asgard.Hermod.UI.Tests;

/// <summary>真实宿主窗口回归测试，验证同步与异步关闭流程。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowLifecycleTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>所有窗口仅在独立 STA 线程中创建。</summary>
    public MainWindowLifecycleTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>没有终端时同步清理也必须等 Closing 返回后再真正关闭。</summary>
    [Fact]
    public Task WindowWithoutTerminals_ClosesWithoutSynchronousReentry() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var window = CreateInvisibleWindow(environment.Shell);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool requestingClose = true;
        bool closedDuringRequest = false;
        window.Closed += (_, _) =>
        {
            closedDuringRequest = requestingClose;
            closed.TrySetResult();
        };
        try
        {
            window.Show();
            window.Close();
            requestingClose = false;
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.False(closedDuringRequest);
            Assert.False(window.IsVisible);
            Assert.Equal(0, environment.Factory.OpenAttempts);
        }
        finally
        {
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>窗口维持 Dispatcher 可用，直到每个终端的异步清理都结束。</summary>
    [Fact]
    public Task WindowWithTerminals_ClosesAfterEverySessionFinishesDisposal() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        environment.Factory.HoldDisposal = true;
        environment.Shell.Workspace.OpenLocalTerminal();
        environment.Shell.Workspace.OpenLocalTerminal();
        var sessions = environment.Factory.Sessions.ToArray();
        var window = CreateInvisibleWindow(environment.Shell);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            window.Close();

            Assert.False(closed.Task.IsCompleted);
            Assert.True(window.IsVisible);
            Assert.All(sessions, session => Assert.Equal(1, session.DisposeCalls));

            sessions[0].ReleaseDisposal();
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(closed.Task.IsCompleted);

            sessions[1].ReleaseDisposal();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            Assert.False(window.IsVisible);
            Assert.All(sessions, session =>
            {
                Assert.True(session.IsDisposed);
                Assert.Equal(1, session.DisposeCalls);
            });
        }
        finally
        {
            environment.Factory.ReleaseAllDisposals();
            if (window.IsVisible)
                window.Close();
        }
    });

    /// <summary>创建真实宿主 HWND，但不显示在桌面或任务栏中。</summary>
    private static Asgard.Hermod.MainWindow CreateInvisibleWindow(ShellViewModel model) => new(model)
    {
        Opacity = 0,
        ShowInTaskbar = false,
        ShowActivated = false,
        WindowStartupLocation = WindowStartupLocation.Manual,
        Left = -10000,
        Top = -10000,
    };
}
