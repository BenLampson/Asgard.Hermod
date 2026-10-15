using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.UI.Controls;
using Asgard.Hermod.UI.Models;
using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Tests;

/// <summary>提示类别、双语文案和主题在离屏布局中验证，不创建桌面窗口或抢占焦点。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MessageDialogPresentationTests(WpfTestFixture fixture)
{
    /// <summary>托盘降级使用警告语义，重复启动使用信息语义；原有错误对话框保留原行为。</summary>
    [Theory]
    [InlineData("en", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("zh-Hans", UiTheme.Dark)]
    public Task NoticesUseAccurateHeadingsAndThemeColors(string language, UiTheme theme) => fixture.RunOffscreenAsync(() =>
    {
        var previous = CultureInfo.CurrentUICulture; var previousDefault = CultureInfo.DefaultThreadCurrentUICulture;
        Text.Initialize(language, CultureInfo.InvariantCulture); var themes = new ThemeService(Application.Current.Resources); themes.SetTheme(theme);
        var warning = MessageDialog.ForTrayUnavailable(); var information = MessageDialog.ForAlreadyRunning(); var error = new MessageDialog("test", "test");
        try
        {
            Assert.Equal(MessageDialogKind.Warning, warning.Kind); Assert.Equal(MessageDialogKind.Information, information.Kind); Assert.Equal(MessageDialogKind.Error, error.Kind);
            Assert.Equal(Text.Get("TrayUnavailableTitle"), Assert.IsType<TextBlock>(warning.FindName("HeadingText")).Text);
            Assert.Equal(Text.Get("HermodAlreadyRunningTitle"), Assert.IsType<TextBlock>(information.FindName("HeadingText")).Text);
            Assert.Equal(Text.Get("ActionFailed"), Assert.IsType<TextBlock>(error.FindName("HeadingText")).Text);
            Assert.Equal("info", Assert.IsType<IconView>(information.FindName("StatusIcon")).Kind);
            Assert.Same(Application.Current.Resources["WarningBrush"], Assert.IsType<IconView>(warning.FindName("StatusIcon")).Foreground);
            Assert.Same(Application.Current.Resources["AccentBrush"], Assert.IsType<IconView>(information.FindName("StatusIcon")).Foreground);
            Assert.Equal(Text.Get("TrayUnavailable"), Assert.IsType<TextBlock>(warning.FindName("MessageText")).Text);
            Assert.DoesNotContain(Text.Get("ActionFailed"), warning.Title);
            Render(warning, $"tray-warning-{language}-{theme}"); Render(information, $"already-running-{language}-{theme}");
        }
        finally { warning.Close(); information.Close(); error.Close(); themes.SetTheme(UiTheme.Light); CultureInfo.CurrentUICulture = previous; CultureInfo.DefaultThreadCurrentUICulture = previousDefault; }
        return Task.CompletedTask;
    });

    /// <summary>只渲染窗口内容树，不调用 Show、激活或任何原生托盘 API。</summary>
    private static void Render(MessageDialog dialog, string name)
    {
        var content = Assert.IsAssignableFrom<FrameworkElement>(dialog.Content);
        dialog.Content = null;
        var surface = new Border { Background = (Brush)Application.Current.Resources["SurfaceBrush"], Child = content };
        surface.Measure(new Size(620, 400)); surface.Arrange(new Rect(0, 0, 620, surface.DesiredSize.Height)); surface.UpdateLayout();
        var message = Assert.IsType<TextBlock>(dialog.FindName("MessageText"));
        Assert.True(message.ActualHeight >= message.FontSize);
        Assert.True(surface.ActualHeight <= 400);
        var origin = message.TranslatePoint(new Point(), surface); Assert.True(origin.Y + message.ActualHeight <= surface.ActualHeight);
        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR"); if (directory is null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(620, (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
