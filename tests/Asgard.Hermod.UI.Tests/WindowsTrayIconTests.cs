using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Asgard.Hermod.UI.Views.Dialogs;
using Asgard.Hermod.UI.Windowing;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 Windows 通知区域注册、GUID 矩形、恢复及菜单视觉交互验证。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WindowsTrayIconTests(WpfTestFixture fixture)
{
    /// <summary>用相同 GUID 重建通知图标，删除后实际恢复，退出后移除系统入口。</summary>
    [Fact]
    public Task RealIcon_RegistersQueriesRectangleAndRecovers() => fixture.RunAsync(async () =>
    {
        Assert.SkipWhen(System.Diagnostics.Process.GetProcessesByName("Asgard.Hermod").Length > 0, "避免与用户正在运行的 Hermod 共用托盘 GUID。");
        Guid testIconId = Guid.NewGuid(); using var tray = new WindowsTrayIcon(testIconId);
        Assert.True(tray.IsAvailable);
        Assert.True(tray.TryGetBounds(out var bounds));
        Assert.True(bounds.Width > 0 && bounds.Height > 0);
        // 仅删除测试自身的 GUID，不重启或终止用户的 Explorer。
        var id = new TrayNotifyData
        {
            Size = Marshal.SizeOf<TrayNotifyData>(),
            Flags = 0x20,
            Guid = testIconId,
            Tip = "",
            Info = "",
            InfoTitle = ""
        };
        Assert.True(TrayNativeMethods.Notify(2, ref id));
        Assert.False(tray.TryGetBounds(out _));
        nint handle = tray.WindowHandle;
        Assert.NotEqual(0, handle);
        SendMessage(handle, TrayNativeMethods.RegisterWindowMessage("TaskbarCreated"), 0, 0);
        await WpfTestFixture.DrainDispatcherAsync();
        Assert.True(tray.TryGetBounds(out bounds));
        Assert.True(bounds.Width > 0);
        tray.Dispose();
        Assert.False(tray.IsAvailable);
        var identifier = new TrayIconIdentifier { Size = Marshal.SizeOf<TrayIconIdentifier>(), Guid = id.Guid };
        Assert.NotEqual(0, TrayNativeMethods.GetRect(ref identifier, out _));
    });

    /// <summary>英中菜单显示真实文字、动作和主题，并以实际图标矩形定位及处理 Esc。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light, "Show main window")]
    [InlineData("zh-Hans", UiTheme.Dark, "显示主窗体")]
    public Task Menu_UsesResourcesThemeAndNativeAnchor(string language, UiTheme theme, string expected) => fixture.RunAsync(async () =>
    {
        Assert.SkipWhen(System.Diagnostics.Process.GetProcessesByName("Asgard.Hermod").Length > 0, "避免与用户正在运行的 Hermod 共用托盘 GUID。");
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        Guid testIconId = Guid.NewGuid(); using var tray = new WindowsTrayIcon(testIconId);
        Assert.True(tray.TryGetBounds(out var bounds));
        var menu = new TrayMenuWindow(true, () => { }, () => { });
        try
        {
            menu.ShowAt(bounds); await WpfTestFixture.DrainDispatcherAsync(); menu.UpdateLayout();
            Assert.Equal(expected, Assert.IsType<TextBlock>(menu.FindName("ToggleLabel")).Text);
            Assert.Equal("tray.main.show", Assert.IsType<Button>(menu.FindName("ToggleButton")).Tag);
            Assert.False(menu.ShowInTaskbar);
            Assert.NotNull(Assert.IsType<System.Windows.Shapes.Path>(menu.FindName("Arrow")).Data);
            string? output = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
            if (output is not null)
            {
                Directory.CreateDirectory(output);
                var bitmap = new RenderTargetBitmap(268, 152, 96, 96, PixelFormats.Pbgra32); bitmap.Render(menu);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "tray-menu-" + language + ".png")); encoder.Save(file);
            }
            await WindowInteractionNativeMethods.PressKeyAsync(menu, 0x1B);
            await WpfTestFixture.DrainDispatcherAsync(); Assert.False(menu.IsVisible);
        }
        finally { if (menu.IsVisible) menu.Close(); themes.SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });

    /// <summary>像素定位覆盖负坐标、不同 DPI、工作区边缘及箭头翻转。</summary>
    [Theory]
    [InlineData(1d)]
    [InlineData(1.5d)]
    [InlineData(2d)]
    public void Placement_StaysWithinWorkArea(double scale)
    {
        var work = new Rect(-1920, 0, 1920, 1040);
        var result = TrayMenuPlacement.Calculate(new Rect(-30, 1040, 24, 24), work, new Size(268, 122), scale);
        Assert.False(result.ArrowOnTop);
        Assert.InRange(result.Position.X, work.Left, work.Right - 268 * scale);
        Assert.InRange(result.Position.Y, work.Top, work.Bottom - 122 * scale);
        Assert.InRange(result.ArrowOffset, 28, 240);
        Assert.True(TrayMenuPlacement.Calculate(new Rect(-1000, -24, 24, 24), work, new Size(268, 122), scale).ArrowOnTop);
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint SendMessage(nint handle, int message, nint wParam, nint lParam);
}
