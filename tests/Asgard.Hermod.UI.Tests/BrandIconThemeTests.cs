using System.Windows.Interop;
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
                VerifyNativeIcon(owner, 0);
                VerifyNativeIcon(owner, 1);
                VerifyNativeIcon(dialog, 0);
                VerifyNativeIcon(dialog, 1);
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

    /// <summary>读取 Windows 实际使用的大、小图标，防止最小帧居中贴入大画布导致主体缩小。</summary>
    private static void VerifyNativeIcon(Window window, int kind)
    {
        var handle = WindowIconNativeMethods.SendMessage(new WindowInteropHelper(window).Handle, 0x007F, kind, 0);
        Assert.NotEqual(nint.Zero, handle);
        var bitmap = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var pixels = ReadPixels(bitmap);
        var minX = bitmap.PixelWidth;
        var minY = bitmap.PixelHeight;
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < bitmap.PixelHeight; y++)
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                // 忽略低透明度的重采样边缘，只计算实际可见轮廓。
                if (pixels[(y * bitmap.PixelWidth + x) * 4 + 3] < 96) continue;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        Assert.True(maxX - minX + 1 >= bitmap.PixelWidth * 0.75, "Native icon must fill its width.");
        Assert.True(maxY - minY + 1 >= bitmap.PixelHeight * 0.85, "Native icon must fill its height.");

        string? directory = Environment.GetEnvironmentVariable("HERMOD_UI_PREVIEW_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;
        Directory.CreateDirectory(directory);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, $"brand-native-{kind}.png"));
        encoder.Save(stream);
    }

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
