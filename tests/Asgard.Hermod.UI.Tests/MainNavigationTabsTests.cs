using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.UI.Views.Shell;
using Text = Asgard.Hermod.Abstractions.Localization.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实主窗体公共页签在不同输出 DPI 下检测衔接像素并导出局部证据。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MainNavigationTabsTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, 1d)]
    [InlineData("en", UiTheme.Light, 1.5d)]
    [InlineData("en", UiTheme.Light, 2d)]
    [InlineData("en", UiTheme.Dark, 1d)]
    [InlineData("en", UiTheme.Dark, 1.5d)]
    [InlineData("en", UiTheme.Dark, 2d)]
    [InlineData("zh-Hans", UiTheme.Light, 1d)]
    [InlineData("zh-Hans", UiTheme.Light, 1.5d)]
    [InlineData("zh-Hans", UiTheme.Light, 2d)]
    [InlineData("zh-Hans", UiTheme.Dark, 1d)]
    [InlineData("zh-Hans", UiTheme.Dark, 1.5d)]
    [InlineData("zh-Hans", UiTheme.Dark, 2d)]
    public Task SelectedTab_HasNoGapBorderOrCaptionSeparator(string language, UiTheme theme, double scale) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        await using var environment = new ShellTestEnvironment(); environment.Themes.SetTheme(theme);
        var window = new Asgard.Hermod.MainWindow(environment.Shell) { Width = 1360, Height = 860 };
        try
        {
            WindowPreviewCapture.Show(window);
            var tabs = WindowTestVisuals.Descendants<MainNavigationTabs>(window).Single();
            foreach (int index in new[] { 0, 1, 2 })
            {
                tabs.SelectedIndex = index; await WindowTestVisuals.UpdateLayoutAsync(window);
                var frame = WindowTestVisuals.Frame(window);
                var item = Assert.IsType<ListBoxItem>(tabs.ItemContainerGenerator.ContainerFromIndex(index));
                var origin = item.TranslatePoint(new Point(), frame);
                var bitmap = new RenderTargetBitmap((int)(1360 * scale), (int)(110 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(frame);
                int x = (int)((origin.X + item.ActualWidth / 2) * scale);
                Color body = ((SolidColorBrush)Application.Current.FindResource("AppBackgroundBrush")).Color;
                byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                for (int y = (int)(40 * scale); y <= (int)(44 * scale); y++)
                {
                    int offset = (y * bitmap.PixelWidth + x) * 4;
                    Assert.Equal(body.B, pixels[offset]); Assert.Equal(body.G, pixels[offset + 1]); Assert.Equal(body.R, pixels[offset + 2]);
                }
                Assert.Empty(WindowTestVisuals.Descendants<ScrollViewer>(tabs));
                string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(directory, $"main-caption-{language}-{theme}-{scale * 100:0}-{index}.png")); encoder.Save(file);
                }
            }
        }
        finally { await window.RequestExitAsync(sessionEnding: true); Text.SetApplicationCulture(null); }
    });
}
