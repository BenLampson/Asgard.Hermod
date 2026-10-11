using System.Windows.Interop;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>比较真实 HWND 的屏幕边界与显示器工作区，防止最大化后界面被裁切。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowBoundsTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用既有的独立 STA 应用与真实界面资源。</summary>
    public MainWindowBoundsTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>最大化必须保留完整标题栏与底部，随后还原原始内容边界。</summary>
    [Fact]
    public Task MaximizeAndRestore_KeepEntireShellInsideMonitorWorkArea() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var window = new Asgard.Hermod.MainWindow(environment.Shell)
        {
            Opacity = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 100,
            Top = 100,
        };
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();
        try
        {
            window.Show();
            await WindowTestVisuals.UpdateLayoutAsync(window);
            var handle = new WindowInteropHelper(window).Handle;
            Assert.NotEqual(IntPtr.Zero, handle);
            var shell = (AppShellView)window.FindName("ShellView");
            var title = Assert.Single(WindowTestVisuals.Descendants<TitleBarView>(window));
            Rect normalBounds = WindowTestVisuals.ScreenBounds(shell);
            Rect normalFrame = WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window));
            Assert.Equal(42d, title.ActualHeight, 3);

            SystemCommands.MaximizeWindowCommand.Execute(null, window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(WindowState.Maximized, window.WindowState);
            AssertMaximizedBounds(window, shell);

            // 收起状态也必须让底部控件留在工作区内。
            environment.Shell.IsSidebarExpanded = false;
            await WindowTestVisuals.UpdateLayoutAsync(window);
            AssertMaximizedBounds(window, shell);
            environment.Shell.IsSidebarExpanded = true;

            SystemCommands.RestoreWindowCommand.Execute(null, window);
            await WindowTestVisuals.UpdateLayoutAsync(window);
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(42d, title.ActualHeight, 3);
            WindowTestVisuals.AssertBounds(normalBounds, WindowTestVisuals.ScreenBounds(shell), "还原后的客户端布局发生偏移。");
            WindowTestVisuals.AssertBounds(normalFrame, WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window)),
                "还原后的整窗边框发生偏移。");
        }
        finally
        {
            window.Close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }
    });

    /// <summary>用屏幕坐标验证显示内容，而不是只检查 WindowState 属性。</summary>
    private static void AssertMaximizedBounds(HermodWindow window, AppShellView shell)
    {
        Rect work = WindowBoundsNativeMethods.GetWorkArea(new WindowInteropHelper(window).Handle);
        var title = Assert.Single(WindowTestVisuals.Descendants<TitleBarView>(window));
        var footer = Assert.Single(WindowTestVisuals.Descendants<SidebarFooterView>(shell));
        var controls = Assert.Single(WindowTestVisuals.Descendants<WindowControls>(title));
        Rect frameBounds = WindowTestVisuals.ScreenBounds(WindowTestVisuals.Frame(window));
        Rect shellBounds = WindowTestVisuals.ScreenBounds(shell);
        Rect titleBounds = WindowTestVisuals.ScreenBounds(title);
        Rect footerBounds = WindowTestVisuals.ScreenBounds(footer);
        Rect controlBounds = WindowTestVisuals.ScreenBounds(controls);
        var dpi = VisualTreeHelper.GetDpi(title);
        string evidence = $"rcWork={work}; Frame={frameBounds}; Body={shellBounds}; Title={titleBounds}; "
            + $"Footer={footerBounds}; WindowControls={controlBounds}; DPI={dpi.DpiScaleX},{dpi.DpiScaleY}";

        Assert.True(titleBounds.Top >= work.Top - 1, "标题栏顶端越过工作区。 " + evidence);
        Assert.Equal(42d, title.ActualHeight, 3);
        Assert.InRange(titleBounds.Height, 42 * dpi.DpiScaleY - 0.5, 42 * dpi.DpiScaleY + 0.5);
        Assert.True(titleBounds.Bottom <= work.Bottom + 1, "标题栏高度没有完整保留。 " + evidence);
        Assert.InRange(footerBounds.Bottom, work.Bottom - 1, work.Bottom + 1);
        Assert.True(controlBounds.Right <= work.Right + 1, "窗口控制按钮越过工作区右边缘。 " + evidence);
        WindowTestVisuals.AssertBounds(work, frameBounds, "最大化整窗边框没有填满显示器工作区。 " + evidence);
        var bodyWork = new Rect(work.Left, work.Top + 42 * dpi.DpiScaleY, work.Width,
            work.Height - 42 * dpi.DpiScaleY);
        WindowTestVisuals.AssertBounds(bodyWork, shellBounds, "主体没有从完整标题栏下方填到工作区底部。 " + evidence);
    }
}
