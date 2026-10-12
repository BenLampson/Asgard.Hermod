using System.Windows.Media.Imaging;
using Asgard.Hermod.UI.Views.Shell;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证透明标志像素，以及现有窗口换肤后的标题栏和原生图标。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class BrandIconThemeTests(WpfTestFixture fixture)
{
    /// <summary>两套配色都保留透明画布和可辨认的主体、强调色。</summary>
    [Theory]
    [InlineData(UiTheme.Light, 36, 50, 71, 163, 101, 28)]
    [InlineData(UiTheme.Dark, 233, 226, 212, 214, 167, 96)]
    public Task ThemeLogo_HasTransparentBackgroundAndDistinctPalette(
        UiTheme theme, byte red, byte green, byte blue, byte accentRed, byte accentGreen, byte accentBlue) => fixture.RunAsync(() =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        try
        {
            themes.SetTheme(theme);
            var logo = Assert.IsAssignableFrom<BitmapSource>(Application.Current.Resources["HermodLogo"]);
            Assert.Equal(512, logo.PixelWidth);
            Assert.Equal(512, logo.PixelHeight);
            var pixels = ReadPixels(logo);
            // 旧版圆角底的四角也透明，检查中央四条边才能发现底色回归。
            foreach (var (x, y) in new[] { (0, 0), (256, 0), (0, 256), (511, 256), (256, 511) })
                Assert.Equal(0, pixels[(y * logo.PixelWidth + x) * 4 + 3]);
            Assert.True(ContainsColor(pixels, red, green, blue));
            Assert.True(ContainsColor(pixels, accentRed, accentGreen, accentBlue));
            var icon = Assert.IsAssignableFrom<BitmapSource>(Application.Current.Resources["HermodWindowIcon"]);
            Assert.Equal(0, ReadPixels(icon)[3]);
        }
        finally
        {
            themes.SetTheme(UiTheme.Light);
        }
        return Task.CompletedTask;
    });

    /// <summary>白天、夜间再回到白天时，两个已打开窗口的标志应同步更新。</summary>
    [Fact]
    public Task ThemeChanges_UpdateExistingCaptionsAndWindowIcons() => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        themes.SetTheme(UiTheme.Light);
        var title = new TitleBarView();
        var owner = new HermodWindow { CaptionContent = title, Content = new Grid(), Width = 1000, Height = 180 };
        var dialog = new HermodDialog { Content = new Grid(), Width = 440, Height = 200 };
        try
        {
            WindowPreviewCapture.Show(owner);
            dialog.Owner = owner;
            WindowPreviewCapture.Show(dialog);
            ImageSource? previousIcon = null;
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark, UiTheme.Light })
            {
                themes.SetTheme(theme);
                await WindowTestVisuals.UpdateLayoutAsync(owner);
                await WindowTestVisuals.UpdateLayoutAsync(dialog);
                var icon = Assert.IsAssignableFrom<ImageSource>(Application.Current.Resources["HermodWindowIcon"]);
                Assert.Same(icon, owner.Icon);
                Assert.Same(icon, dialog.Icon);
                Assert.Same(Application.Current.Resources["HermodLogo"], Assert.IsType<Image>(title.FindName("BrandLogo")).Source);
                var caption = Assert.Single(WindowTestVisuals.Descendants<HermodTitleBar>(dialog));
                Assert.Same(icon, Assert.Single(WindowTestVisuals.Descendants<Image>(caption)).Source);
                if (previousIcon is not null) Assert.NotSame(previousIcon, icon);
                previousIcon = icon;
                await WindowPreviewCapture.VerifyAsync(owner, $"brand-shell-{theme}");
                await WindowPreviewCapture.VerifyAsync(dialog, $"brand-dialog-{theme}");
            }
        }
        finally
        {
            dialog.Close();
            owner.Close();
            themes.SetTheme(UiTheme.Light);
        }
    });

    /// <summary>转换为非预乘 BGRA，使透明度和配色断言不依赖原始编码格式。</summary>
    private static byte[] ReadPixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        return pixels;
    }

    /// <summary>只检查实色内部像素，忽略轮廓抗锯齿带来的色值变化。</summary>
    private static bool ContainsColor(byte[] pixels, byte red, byte green, byte blue)
    {
        for (var index = 0; index < pixels.Length; index += 4)
            if (pixels[index] == blue && pixels[index + 1] == green && pixels[index + 2] == red && pixels[index + 3] == 255)
                return true;
        return false;
    }
}
