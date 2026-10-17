using System.Windows.Interop;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证原生圆角在尺寸和主题变化后不会退回硬裁切。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainWindowFrameTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>复用真实 WPF 资源与独立 STA 线程。</summary>
    public MainWindowFrameTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>创建、缩放、最大化和还原均不能重新产生带像素台阶的窗口区域。</summary>
    [Fact]
    public Task ResizeAndRestore_KeepWindowFreeOfRegionClipping() => RunWithWindowAsync(async (window, _) =>
    {
        AssertNoWindowRegion(window);
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            Assert.Equal(2u, GetDwmAttribute(window, 33)); // DWMWCP_ROUND

        window.Width = 1080;
        window.Height = 680;
        await UpdateNativeLayoutAsync(window);
        AssertNoWindowRegion(window);

        SystemCommands.MaximizeWindowCommand.Execute(null, window);
        await UpdateNativeLayoutAsync(window);
        Assert.Equal(WindowState.Maximized, window.WindowState);
        AssertNoWindowRegion(window);

        SystemCommands.RestoreWindowCommand.Execute(null, window);
        await UpdateNativeLayoutAsync(window);
        Assert.Equal(WindowState.Normal, window.WindowState);
        AssertNoWindowRegion(window);
    });

    /// <summary>动态资源替换后仍须保留原生圆角，不能重新产生带台阶的窗口区域。</summary>
    [Fact]
    public Task ThemeChanges_KeepWindowFreeOfRegionClipping() => RunWithWindowAsync(async (window, environment) =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return;

        AssertNoWindowRegion(window);
        Assert.Equal(2u, GetDwmAttribute(window, 33));
        environment.Themes.SetTheme(UiTheme.Dark);
        await UpdateNativeLayoutAsync(window);
        AssertNoWindowRegion(window);
        Assert.Equal(2u, GetDwmAttribute(window, 33));

        environment.Themes.SetTheme(UiTheme.Light);
        await UpdateNativeLayoutAsync(window);
        AssertNoWindowRegion(window);
        Assert.Equal(2u, GetDwmAttribute(window, 33));
    });

    /// <summary>不可调整大小的弹窗也使用同一条原生圆角路径，正文不能叠加另一层轮廓。</summary>
    [Fact]
    public Task Dialog_UsesNativeFrameWithoutAdditionalRoundedBorder() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var dialog = new HermodDialog
        {
            Width = 440,
            Height = 320,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        dialog.SourceInitialized += HideTestWindow;
        try
        {
            dialog.Show();
            await UpdateNativeLayoutAsync(dialog);
            AssertNoWindowRegion(dialog);
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
                Assert.Equal(2u, GetDwmAttribute(dialog, 33));
            var frame = Assert.IsType<Border>(dialog.Template.FindName("PART_WindowFrame", dialog));
            Assert.Equal(new Thickness(0), frame.BorderThickness);
            Assert.Equal(new CornerRadius(0), frame.CornerRadius);
        }
        finally
        {
            dialog.Close();
        }
    });

    /// <summary>窗口放在屏幕之外，保持不透明以免 layered window 改变 DWM 的圆角策略。</summary>
    private Task RunWithWindowAsync(Func<Asgard.Hermod.MainWindow, ShellTestEnvironment, Task> action) =>
        _fixture.RunAsync(async () =>
        {
            await using var environment = new ShellTestEnvironment();
            var window = new Asgard.Hermod.MainWindow(environment.Shell)
            {
                ShowInTaskbar = false,
                ShowActivated = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
            };
            window.SourceInitialized += HideTestWindow;
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Closed += (_, _) => closed.TrySetResult();
            try
            {
                window.Show();
                await UpdateNativeLayoutAsync(window);
                await action(window, environment);
            }
            finally
            {
                window.Close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            }
        });

    /// <summary>保留不透明 HWND 的合成行为，但最大化测试不会闪现或遮挡用户桌面。</summary>
    private static void HideTestWindow(object? sender, EventArgs args)
    {
        uint hidden = 1;
        int result = WindowFrameNativeMethods.DwmSetWindowAttribute(new WindowInteropHelper((Window)sender!).Handle,
            13, ref hidden, sizeof(uint)); // DWMWA_CLOAK
        Assert.Equal(0, result);
    }

    /// <summary>窗口区域必须为空，单纯把 CornerRadius 设为零仍会留下矩形 HRGN。</summary>
    private static void AssertNoWindowRegion(Window window) => Assert.Equal(0,
        WindowFrameNativeMethods.GetWindowRgnBox(new WindowInteropHelper(window).Handle, out _));

    /// <summary>原生查询失败应使回归失败，不能把缺失的属性当作默认值。</summary>
    private static uint GetDwmAttribute(Window window, int attribute)
    {
        int result = WindowFrameNativeMethods.DwmGetWindowAttribute(new WindowInteropHelper(window).Handle,
            attribute, out uint value, sizeof(uint));
        Assert.Equal(0, result);
        return value;
    }

    /// <summary>等待原生尺寸事件和 WPF 布局全部生效。</summary>
    private static async Task UpdateNativeLayoutAsync(Window window)
    {
        await WpfTestFixture.DrainDispatcherAsync();
        window.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync();
    }
}
