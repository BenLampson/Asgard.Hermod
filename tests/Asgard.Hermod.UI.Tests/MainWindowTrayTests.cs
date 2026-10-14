using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实主窗体与真实导航工作窗口联合验证托盘生命周期。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowTrayTests(WpfTestFixture fixture)
{
    /// <summary>关闭主窗体只隐藏；正常最小化、重复激活和工作会话互不破坏。</summary>
    [Fact]
    public Task CloseMain_HidesAndRecallsWithoutClosingWork() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        var tray = new Tray(); var preferences = new Preferences();
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray, preferences, confirmExit: _ => Task.FromResult(true));
        main.Show();
        Assert.True(WindowCloseBehavior.GetHideToTray(main));
        environment.Shell.Workspace.OpenInstance(environment.Local);
        var work = Assert.Single(host.Windows);
        main.Close();
        Assert.False(main.IsVisible); Assert.False(main.ShowInTaskbar);
        Assert.True(work.IsVisible); Assert.True(work.ShowInTaskbar); Assert.Null(work.Owner);
        Assert.False(work.Page.IsClosed); Assert.Equal(0, environment.Factory.Sessions[0].DisposeCalls);
        Assert.Equal(1, preferences.Saves); Assert.Equal(1, tray.Hints);
        tray.Activate();
        Assert.True(main.IsVisible); Assert.True(main.ShowInTaskbar);
        main.WindowState = WindowState.Minimized;
        Assert.True(main.ShowInTaskbar);
        tray.Activate();
        Assert.NotEqual(WindowState.Minimized, main.WindowState);
        main.Close(); Assert.Equal(1, tray.Hints);
        Assert.True(await main.RequestExitAsync());
        Assert.True(tray.Disposed); Assert.All(environment.Factory.Sessions, session => Assert.Equal(1, session.DisposeCalls));
    });

    /// <summary>全应用退出取消不关闭窗口；已移出页签的单窗异步清理也必须等待。</summary>
    [Fact]
    public Task Exit_WaitsForAlreadyClosingWindowAndCancellationKeepsEverythingAlive() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        var tray = new Tray(); bool accept = false, finished = false;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray, new Preferences(),
            () => { finished = true; return Task.CompletedTask; }, _ => Task.FromResult(accept));
        main.Show(); environment.Factory.HoldDisposal = true; environment.Shell.Workspace.OpenLocalTerminal();
        Assert.False(await main.RequestExitAsync());
        Assert.True(Assert.Single(host.Windows).IsVisible); Assert.False(tray.Disposed);
        Assert.All(environment.Factory.Sessions, session => Assert.Equal(0, session.DisposeCalls));
        Assert.Single(host.Windows).Close();
        Assert.Equal(1, environment.Shell.Workspace.PendingCloseCount);
        accept = true;
        Task<bool> exiting = main.RequestExitAsync();
        Assert.False(exiting.IsCompleted); Assert.False(finished); Assert.False(tray.Disposed);
        environment.Factory.ReleaseAllDisposals();
        Assert.True(await exiting);
        Assert.True(finished); Assert.True(tray.Disposed);
        Assert.All(environment.Factory.Sessions, session => Assert.Equal(1, session.DisposeCalls));
    });

    /// <summary>模态输入仍在原对话框中；激活主窗体不会替换或关闭它。</summary>
    [Fact]
    public Task Recall_PreservesOwnedDialogInput() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var tray = new Tray(); var main = new Asgard.Hermod.MainWindow(environment.Shell, tray: tray);
        main.Show();
        var input = new TextBox { Text = "unsaved input" };
        var dialog = new HermodDialog { Owner = main, Content = input };
        _ = dialog.Dispatcher.BeginInvoke(() =>
        {
            tray.Activate(); Assert.True(dialog.IsVisible); Assert.Equal("unsaved input", input.Text); dialog.Close();
        });
        dialog.ShowDialog();
        await main.RequestExitAsync();
    });

    /// <summary>通知区域失效立即恢复主窗口及任务栏，关闭退回正常退出。</summary>
    [Fact]
    public Task TrayFailure_RestoresMainAndNormalClose() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var tray = new Tray(); var main = new Asgard.Hermod.MainWindow(environment.Shell, tray: tray);
        main.Show(); main.Close(); Assert.False(main.IsVisible);
        _ = main.Dispatcher.BeginInvoke(() =>
        {
            var dialog = Application.Current.Windows.OfType<Asgard.Hermod.UI.Views.Dialogs.MessageDialog>().Single();
            try
            {
                Assert.True(main.IsVisible); Assert.True(main.ShowInTaskbar);
                Assert.False(WindowCloseBehavior.GetHideToTray(main));
            }
            finally { dialog.Close(); }
        });
        tray.SetAvailable(false);
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        main.Closed += (_, _) => closed.SetResult(); main.Close(); await closed.Task;
        Assert.True(tray.Disposed);
    });

    /// <summary>系统会话结束跳过交互确认并实际释放，不转为托盘隐藏。</summary>
    [Fact]
    public Task SessionEnding_CleansUpWithoutHidingOrConfirming() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace); var tray = new Tray();
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray,
            confirmExit: _ => throw new InvalidOperationException("System exit must not ask for confirmation."));
        main.Show(); environment.Shell.Workspace.OpenLocalTerminal();
        Assert.True(await main.RequestExitAsync(sessionEnding: true));
        Assert.False(main.IsVisible); Assert.True(tray.Disposed); Assert.Equal(0, tray.Hints);
        Assert.True(environment.Factory.Sessions[0].IsDisposed);
    });

    /// <summary>独立工作窗口的模态链也能被主窗体激活命中，托盘退出不叠加预检。</summary>
    [Fact]
    public Task WorkWindowModal_RecallAndExitPreserveOriginalDialog() => fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace); var tray = new Tray(); int confirms = 0;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray,
            confirmExit: _ => { confirms++; return Task.FromResult(true); });
        main.Show(); environment.Shell.Workspace.OpenLocalTerminal();
        var work = Assert.Single(host.Windows); var input = new TextBox { Text = "keep work draft" };
        var dialog = new HermodDialog { Owner = work, Content = input };
        await ModalWindowTestInteraction.ShowAsync(dialog, async () =>
        {
            Assert.False(WindowInteractionNativeMethods.IsEnabled(main));
            Assert.Same(dialog, WindowActivation.FindModal());
            tray.Activate();
            Assert.False(await main.RequestExitAsync());
            Assert.Equal(0, confirms); Assert.True(dialog.IsVisible); Assert.Equal("keep work draft", input.Text);
            Assert.False(work.Page.IsClosed); Assert.Equal(0, environment.Factory.Sessions[0].DisposeCalls);
        });
        Assert.True(await main.RequestExitAsync()); Assert.Equal(1, confirms);
    });

    private sealed class Preferences : ITrayPreferenceStore
    {
        internal int Saves;
        public bool HasShownHideHint() => Saves > 0;
        public void MarkHideHintShown() => Saves++;
    }
    private sealed class Tray : ITrayIcon
    {
        internal int Hints;
        internal bool Disposed;
        public bool IsAvailable { get; private set; } = true;
        public event Action? Activated;
        public event Action? MenuRequested { add { } remove { } }
        public event Action? AvailabilityChanged;
        public bool TryGetBounds(out Rect rectangle) { rectangle = new Rect(10, 10, 20, 20); return true; }
        public void ShowHiddenHint() => Hints++;
        public void Dispose() { Disposed = true; IsAvailable = false; }
        internal void Activate() => Activated?.Invoke();
        internal void SetAvailable(bool available) { IsAvailable = available; AvailabilityChanged?.Invoke(); }
    }
}
