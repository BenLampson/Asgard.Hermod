using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证悬浮提示的真实文字、背景与长名称布局在切换主题后仍匹配。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ToolTipThemeTests(WpfTestFixture fixture)
{
    /// <summary>同一个提示实例往返换肤，长标识符完整换行，不依赖系统提示配色。</summary>
    [Theory]
    [InlineData("qishu_drawing\n普通表")]
    [InlineData("qishu_drawing\nOrdinary table")]
    [InlineData("qishu_drawing_with_a_very_long_name_that_must_wrap_without_losing_any_characters_or_hiding_the_table_type\nPartitioned table")]
    public Task ThemeSwitch_PreservesReadableContentAndBounds(string content) => fixture.RunOffscreenAsync(async () =>
    {
        var themes = new ThemeService(Application.Current.Resources);
        var tip = new ToolTip { Content = content };
        // 未打开的浮层不在 Application.Windows 中，显式接入同一资源字典以接收换肤失效通知。
        tip.Resources.MergedDictionaries.Add(Application.Current.Resources);
        try
        {
            foreach (var theme in new[] { UiTheme.Dark, UiTheme.Light, UiTheme.Dark })
            {
                themes.SetTheme(theme);
                tip.Measure(new(800, 600)); tip.Arrange(new Rect(tip.DesiredSize)); tip.UpdateLayout();
                await WpfTestFixture.DrainDispatcherAsync(); tip.UpdateLayout();
                var label = Assert.Single(WindowTestVisuals.Descendants<TextBlock>(tip));
                Assert.Equal(content, label.Text);
                Assert.Same(Application.Current.Resources["TextPrimaryBrush"], label.Foreground);
                var chrome = Assert.Single(WindowTestVisuals.Descendants<Border>(tip));
                Assert.Same(Application.Current.Resources["SurfaceBrush"], chrome.Background);
                Assert.Same(Application.Current.Resources["LineSoftBrush"], chrome.BorderBrush);
                Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                Assert.InRange(tip.ActualWidth, 60, 480);
                Assert.InRange(tip.ActualHeight, 35, 300);
                Assert.True(Contrast(label.Foreground, chrome.Background) >= 4.5);
                Assert.False(tip.Focusable); Assert.Null(PresentationSource.FromVisual(tip));
                Capture(tip, $"tooltip-{theme}-{(content.Length > 100 ? "long" : content.Contains('普') ? "zh" : "en")}");
            }
        }
        finally { themes.SetTheme(UiTheme.Light); }
    });

    /// <summary>直接检查绘制文字与底色的相对亮度，避免只验证资源键而漏掉低对比度。</summary>
    private static double Contrast(Brush foreground, Brush background)
    {
        double first = Luminance(Assert.IsType<SolidColorBrush>(foreground).Color);
        double second = Luminance(Assert.IsType<SolidColorBrush>(background).Color);
        return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05);
    }

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            double value = channel / 255d;
            return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
        }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    /// <summary>按需保存真实 WPF 模板渲染，用于本地视觉核对。</summary>
    private static void Capture(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(view.ActualWidth), (int)Math.Ceiling(view.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(stream);
    }
}
