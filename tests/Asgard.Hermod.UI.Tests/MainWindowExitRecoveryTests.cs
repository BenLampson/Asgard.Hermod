using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>退出异常不能遗留冻结的主窗口、已关闭的错误 Owner 或未观察的窗口异常。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowExitRecoveryTests(WpfTestFixture fixture)
{
    /// <summary>关闭预检失败后恢复全部输入，安全提示可关闭，下一次退出可以成功。</summary>
    [Theory]
    [InlineData("en", false)]
    [InlineData("zh-Hans", false)]
    [InlineData("en", true)]
    [InlineData("zh-Hans", true)]
    public Task FailedPreflightRestoresInputAndAllowsRetry(string culture, bool failWhileDisabling) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(System.Globalization.CultureInfo.GetCultureInfo(culture));
        await using var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        bool fail = true;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, confirmExit: _ =>
            fail && !failWhileDisabling ? Task.FromException<bool>(new IOException("secret-test-error")) : Task.FromResult(true));
        main.Show(); environment.Shell.Workspace.OpenLocalTerminal();
        ((UIElement)main.CaptionContent!).IsEnabledChanged += (_, args) =>
        {
            if (fail && failWhileDisabling && args.NewValue is false) throw new IOException("secret-test-error");
        };
        string? heading = null, message = null;
        var dismiss = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
        {
            if (Application.Current.Windows.OfType<MessageDialog>().SingleOrDefault() is not { IsVisible: true } dialog) return;
            heading = ((TextBlock)dialog.FindName("HeadingText")).Text;
            message = ((TextBlock)dialog.FindName("MessageText")).Text;
            dialog.Close();
        }, Dispatcher.CurrentDispatcher);
        try
        {
            Assert.False(await main.RequestExitAsync());
            Assert.Equal(Text.Get("ExitPreparationFailedTitle"), heading);
            Assert.Equal(Text.Get("ExitPreparationFailed"), message);
            Assert.False(main.IsExitCommitted); Assert.False(environment.Shell.Workspace.IsExitPending);
            Assert.True(((UIElement)main.CaptionContent!).IsEnabled); Assert.True(((UIElement)main.Content).IsEnabled);
            Assert.True(WindowInteractionNativeMethods.IsEnabled(main));
            Assert.True(((UIElement)Assert.Single(host.Windows).Content).IsEnabled);
            Assert.Equal(0, Assert.Single(environment.Factory.Sessions).DisposeCalls);
            fail = false; Assert.True(await main.RequestExitAsync());
        }
        finally { dismiss.Stop(); Text.SetApplicationCulture(null); }
    });

    /// <summary>已提交退出后即使会话异步释放失败，也必须继续回收其他会话、托盘与宿主。</summary>
    [Fact]
    public Task FailedCleanupStillFinishesExitWithoutModalError() => fixture.RunAsync(async () =>
    {
        var environment = new ShellTestEnvironment();
        using var host = new WorkspaceWindowHost(environment.Shell.Workspace);
        var failed = new FailedPage();
        var key = new DataTemplateKey(typeof(FailedPage));
        Application.Current.Resources[key] = new DataTemplate { VisualTree = new FrameworkElementFactory(typeof(Border)) };
        environment.Shell.Workspace.Tabs.Add(failed);
        var failedWindow = new WorkspaceWindow(environment.Shell.Workspace, failed); failedWindow.Show();
        var tray = new TestTray(); int finishes = 0, notices = 0;
        var main = new Asgard.Hermod.MainWindow(environment.Shell, host, tray,
            finishExit: () => { finishes++; return Task.CompletedTask; }, confirmExit: _ => Task.FromResult(true));
        main.Show(); environment.Shell.Workspace.OpenLocalTerminal();
        var dismiss = new DispatcherTimer(TimeSpan.FromMilliseconds(20), DispatcherPriority.Normal, (_, _) =>
        {
            foreach (var notice in Application.Current.Windows.OfType<MessageDialog>().ToArray()) { notices++; notice.Close(); }
        }, Dispatcher.CurrentDispatcher);
        try
        {
            // 模拟模块在预检后回收所属窗口，之后错误提示不能再附着该窗口。
            main.ExitCommitted += () => DialogOwner.Override = failedWindow;
            Assert.True(await main.RequestExitAsync());
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(0, notices); Assert.Equal(1, finishes); Assert.True(tray.Disposed);
            Assert.False(main.IsVisible); Assert.False(failedWindow.IsVisible); Assert.Empty(host.Windows);
            Assert.Equal(1, failed.Releases); Assert.True(Assert.Single(environment.Factory.Sessions).IsDisposed);
            Assert.Null(DialogOwner.Override);
        }
        finally { dismiss.Stop(); Application.Current.Resources.Remove(key); }
        // 工作区保留失败任务供诊断；不要把预期故障交给测试环境的自动释放。
        await Assert.ThrowsAsync<IOException>(() => environment.Shell.Workspace.DisposeAsync().AsTask());
    });

    private sealed class FailedPage() : WorkspacePageViewModel("cleanup-failure", "Test", "terminal")
    {
        internal int Releases;
        protected override async ValueTask ReleaseAsync()
        {
            Releases++; await Dispatcher.Yield(DispatcherPriority.Background);
            throw new IOException("secret-test-error");
        }
    }

    private sealed class TestTray : ITrayIcon
    {
        internal bool Disposed;
        public bool IsAvailable => !Disposed;
        public event Action? Activated { add { } remove { } }
        public event Action? MenuRequested { add { } remove { } }
        public event Action? AvailabilityChanged { add { } remove { } }
        public bool TryGetBounds(out Rect rectangle) { rectangle = default; return false; }
        public void ShowHiddenHint() { }
        public void Dispose() => Disposed = true;
    }
}
