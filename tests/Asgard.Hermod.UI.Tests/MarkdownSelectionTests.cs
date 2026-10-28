using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>用户消息和 Markdown 正文的选区必须保留可见文字。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MarkdownSelectionTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SelectedMessageRetainsVisibleGlyphs(bool markdown) => fixture.RunAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var view = new MarkdownView { Source = "Readable selected message 123", IsMarkdownEnabled = markdown, FontSize = 20 };
        var background = new Border { Child = view };
        background.SetResourceReference(Border.BackgroundProperty, "AppBackgroundBrush");
        var window = new HermodWindow { Content = background, Width = 500, Height = 180 };
        try
        {
            WindowPreviewCapture.Show(window);
            foreach (var theme in new[] { UiTheme.Light, UiTheme.Dark })
            {
                themes.SetTheme(theme);
                await WindowTestVisuals.UpdateLayoutAsync(window);
                view.Focus(); view.SelectAll();
                await WindowTestVisuals.UpdateLayoutAsync(window);
                Assert.Contains("Readable selected message", view.Selection.Text);
                VerifyRenderedSelection(background);
            }
        }
        finally { window.Close(); themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>检查实际选区像素，不只验证画刷属性。</summary>
    private static void VerifyRenderedSelection(FrameworkElement view)
    {
        int width = (int)Math.Ceiling(view.ActualWidth), height = (int)Math.Ceiling(view.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var pixels = new byte[width * height * 4]; bitmap.CopyPixels(pixels, width * 4, 0);
        var selected = new List<(int X, int Y)>();
        var colors = new Dictionary<int, int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                if (pixels[index] - pixels[index + 2] > 35 && pixels[index] - pixels[index + 1] > 20)
                {
                    selected.Add((x, y));
                    int rgb = (pixels[index + 2] << 16) | (pixels[index + 1] << 8) | pixels[index];
                    colors[rgb] = colors.GetValueOrDefault(rgb) + 1;
                }
            }
        Assert.NotEmpty(selected);
        int fill = colors.MaxBy(entry => entry.Value).Key;
        double highlight = Luminance((byte)(fill >> 16), (byte)(fill >> 8), (byte)fill);
        int readable = 0, left = selected.Min(point => point.X), right = selected.Max(point => point.X),
            top = selected.Min(point => point.Y), bottom = selected.Max(point => point.Y);
        for (int y = top; y <= bottom; y++)
            for (int x = left; x <= right; x++)
            {
                int index = (y * width + x) * 4;
                double glyph = Luminance(pixels[index + 2], pixels[index + 1], pixels[index]);
                if ((Math.Max(highlight, glyph) + 0.05) / (Math.Min(highlight, glyph) + 0.05) >= 4.5) readable++;
            }
        Assert.True(readable >= 5, "Selected message glyphs must remain readable against the highlight.");
    }

    private static double Luminance(byte red, byte green, byte blue)
    {
        static double Linear(byte value) { double channel = value / 255d; return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4); }
        return 0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);
    }
}
