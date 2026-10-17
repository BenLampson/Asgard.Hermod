using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证小尺寸数据库标识的实际像素对比度，以及现有控件换肤、复用后的颜色。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseIconThemeTests(WpfTestFixture fixture)
{
    /// <summary>调用方通用前景色不能使数据库品牌变暗；运行时换肤必须重绘现有图标。</summary>
    [Theory]
    [InlineData("brand-mysql", 18)]
    [InlineData("brand-mysql", 24)]
    [InlineData("brand-postgresql", 18)]
    [InlineData("brand-postgresql", 24)]
    public Task DatabaseMarks_RemainVisibleAcrossThemeChanges(string kind, double size) => fixture.RunOffscreenAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var icon = new IconView { Kind = kind, Width = size, Height = size, Foreground = Brushes.Black };
        var tile = new Border { Width = 34, Height = 34, Child = icon };
        tile.Resources.MergedDictionaries.Add(Application.Current.Resources);
        tile.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        try
        {
            byte[]? previous = null;
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark, UiTheme.Light })
            {
                themes.SetTheme(theme);
                await WpfTestFixture.DrainDispatcherAsync();
                var pixels = Render(tile);
                var background = ((SolidColorBrush)tile.Background).Color;
                int visible = Enumerable.Range(0, 34 * 34).Count(index =>
                    Contrast(Color.FromRgb(pixels[index * 4 + 2], pixels[index * 4 + 1], pixels[index * 4]), background) >= 3);
                Assert.True(visible >= size, $"{kind}, {theme}, {size}px: only {visible} distinguishable pixels.");
                if (previous is not null) Assert.False(previous.SequenceEqual(pixels));
                previous = pixels;
            }
        }
        finally { themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>复用控件切换数据库类型后更新品牌色，回到通用图标后仍尊重调用方颜色。</summary>
    [Fact]
    public Task ReusingIcon_RestoresGenericForeground() => fixture.RunOffscreenAsync(() =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        try
        {
            themes.SetTheme(UiTheme.Dark);
            var icon = new IconView { Kind = "brand-mysql", Width = 24, Height = 24, Foreground = Brushes.Red };
            var tile = new Border { Width = 34, Height = 34, Background = Brushes.White, Child = icon };
            var mysql = Render(tile);
            icon.Kind = "brand-postgresql";
            Assert.False(mysql.SequenceEqual(Render(tile)));
            icon.Kind = "database";
            var pixels = Render(tile);
            Assert.Contains(Enumerable.Range(0, 34 * 34), index =>
                pixels[index * 4 + 2] > 240 && pixels[index * 4] < 16 && pixels[index * 4 + 1] < 16);
        }
        finally { themes.SetTheme(UiTheme.Light); }
        return Task.CompletedTask;
    });

    /// <summary>测量真实 WPF 填充与抗锯齿后的像素，不以资源键或颜色常量代替可见性。</summary>
    private static byte[] Render(Border tile)
    {
        tile.Measure(new(34, 34)); tile.Arrange(new(0, 0, 34, 34)); tile.UpdateLayout();
        var bitmap = new RenderTargetBitmap(34, 34, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(tile);
        var pixels = new byte[34 * 34 * 4]; bitmap.CopyPixels(pixels, 34 * 4, 0);
        return pixels;
    }

    private static double Contrast(Color first, Color second)
    {
        double a = Luminance(first), b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static double Luminance(Color color)
    {
        static double Channel(byte component)
        {
            double value = component / 255d;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
    }
}
