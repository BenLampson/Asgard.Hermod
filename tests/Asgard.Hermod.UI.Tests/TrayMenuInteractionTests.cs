using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>定向 HWND 鼠标消息和原生按键区分普通、悬停及键盘导航，不移动桌面指针。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TrayMenuInteractionTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, false)]
    [InlineData("en", UiTheme.Dark, false)]
    [InlineData("zh-Hans", UiTheme.Light, false)]
    [InlineData("zh-Hans", UiTheme.Dark, false)]
    [InlineData("en", UiTheme.Light, true)]
    [InlineData("en", UiTheme.Dark, true)]
    [InlineData("zh-Hans", UiTheme.Light, true)]
    [InlineData("zh-Hans", UiTheme.Dark, true)]
    public Task Menu_HasNoInitialSelectionAndRetainsMouseAndKeyboardActions(string language, UiTheme theme, bool physicalPointer) => fixture.RunAsync(async () =>
    {
        Assert.SkipWhen(physicalPointer && Environment.GetEnvironmentVariable("HERMOD_RUN_DESKTOP_POINTER_TESTS") != "1",
            "物理指针集成测试仅在用户明确允许桌面验收并设置 HERMOD_RUN_DESKTOP_POINTER_TESTS=1 时运行。");
        Assert.SkipWhen(System.Diagnostics.Process.GetProcessesByName("Asgard.Hermod").Length > 0, "避免共用用户的托盘图标。");
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        using var tray = new WindowsTrayIcon(); Assert.True(tray.TryGetBounds(out var icon));
        int toggleCalls = 0, exitCalls = 0;
        CursorPoint original = default; if (physicalPointer) Assert.True(GetCursorPos(out original));
        var menu = new TrayMenuWindow(true, () => toggleCalls++, () => exitCalls++);
        // 在窗口销毁前归还桌面指针；关闭后的 SetCursorPos 在当前测试环境可能失败。
        if (physicalPointer) menu.Closing += (_, _) => SetCursorPos(original.X, original.Y);
        Exception? failure = null;
        try
        {
            menu.ShowAt(icon); await WindowTestVisuals.UpdateLayoutAsync(menu);
            var toggle = Assert.IsType<Button>(menu.FindName("ToggleButton")); var exit = Assert.IsType<Button>(menu.FindName("ExitButton"));
            Move(new Point(30, 12)); await UntilAsync(() => !toggle.IsMouseOver && !exit.IsMouseOver);
            Assert.False(menu.KeyboardNavigationActive); Assert.False(toggle.IsKeyboardFocused); Assert.False(exit.IsKeyboardFocused);
            Assert.Equal(122, menu.ActualHeight); Assert.Equal(new Thickness(0), toggle.BorderThickness); Assert.Null(toggle.FocusVisualStyle);
            Assert.Same(Brushes.Transparent, toggle.Background); Assert.Same(Brushes.Transparent, exit.Background);
            Assert.DoesNotContain(WindowTestVisuals.Descendants<TextBlock>(menu), text => text.Text == Text.Get("AsgardHermod"));
            Save(menu, $"tray-menu-{language}-{theme}-normal");
            if (physicalPointer)
            {
                Move(toggle.TranslatePoint(new Point(toggle.ActualWidth / 2, toggle.ActualHeight / 2), menu)); await UntilAsync(() => toggle.IsMouseOver);
                Assert.Same(Application.Current.Resources["AccentWeakBrush"], toggle.Background); Assert.Equal(new Thickness(0), toggle.BorderThickness);
                Save(menu, $"tray-menu-{language}-{theme}-hover");
                Move(new Point(30, 12)); await UntilAsync(() => !toggle.IsMouseOver);
            }
            await WindowInteractionNativeMethods.DrainNativeMessagesAsync();
            await WindowInteractionNativeMethods.PressKeyAsync(menu, 0x09); await WpfTestFixture.DrainDispatcherAsync();
            Assert.True(menu.KeyboardNavigationActive, "Tab 导航不能被合成鼠标移动清除。");
            Assert.True(toggle.IsKeyboardFocused, WindowInteractionNativeMethods.DescribeFocus(menu, toggle)); Assert.Equal(new Thickness(1), toggle.BorderThickness);
            Save(menu, $"tray-menu-{language}-{theme}-keyboard");
            await WindowInteractionNativeMethods.PressKeyAsync(menu, 0x28); Assert.True(exit.IsKeyboardFocused);
            await WindowInteractionNativeMethods.PressKeyAsync(menu, 0x0D); await WpfTestFixture.DrainDispatcherAsync();
            Assert.False(menu.IsVisible); Assert.Equal(1, exitCalls); Assert.Equal(0, toggleCalls);
        }
        catch (Exception error) { failure = error; throw; }
        finally
        {
            if (menu.IsVisible) menu.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null);
            if (physicalPointer)
            {
                bool moved = SetCursorPos(original.X, original.Y); int error = Marshal.GetLastWin32Error();
                bool restored = GetCursorPos(out var current) && current.X == original.X && current.Y == original.Y;
                string evidence = $"Pointer restore: Move={moved}; Win32={error}; Expected={original.X},{original.Y}; Actual={current.X},{current.Y}";
                if (failure is null) Assert.True(restored, evidence); else if (!restored) TestContext.Current.TestOutputHelper?.WriteLine(evidence);
            }
        }
        void Move(Point point)
        {
            if (!physicalPointer) { WindowInteractionNativeMethods.PostPointerMove(menu, point); return; }
            Point screen = menu.PointToScreen(point);
            Assert.True(SetCursorPos((int)screen.X, (int)screen.Y), $"物理指针移动失败，Win32={Marshal.GetLastWin32Error()}。");
        }
    });
    private static async Task UntilAsync(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition()) { Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), "托盘菜单鼠标状态未就绪。"); await Task.Delay(10, TestContext.Current.CancellationToken); }
    }
    private static void Save(Window window, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X; public int Y; }
    [DllImport("user32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll", SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetCursorPos(int x, int y);
}
