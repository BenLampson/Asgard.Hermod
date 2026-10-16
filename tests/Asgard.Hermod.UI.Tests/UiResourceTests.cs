using System.Windows.Media.Imaging;

namespace Asgard.Hermod.UI.Tests;

/// <summary>检查真实资源在加载、换肤和字体打包后的可用性。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class UiResourceTests
{
    private readonly WpfTestFixture _fixture;

    /// <summary>使用真实程序集字体、样式和颜色资源。</summary>
    public UiResourceTests(WpfTestFixture fixture) => _fixture = fixture;

    /// <summary>主题变化更新已有控件而不累积颜色字典。</summary>
    [Fact]
    public Task ThemeChange_UpdatesExistingControlWithoutAccumulatingResources() => _fixture.RunAsync(async () =>
    {
        await using var environment = new ShellTestEnvironment();
        var text = new TextBlock();
        text.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimaryBrush");
        var window = new Window
        {
            Content = text,
            Opacity = 0,
            ShowInTaskbar = false,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000,
        };
        try
        {
            window.Show();
            await WpfTestFixture.DrainDispatcherAsync();
            Color light = Assert.IsType<SolidColorBrush>(text.Foreground).Color;
            int dictionaryCount = Application.Current.Resources.MergedDictionaries.Count;

            environment.Themes.SetTheme(UiTheme.Dark);
            await WpfTestFixture.DrainDispatcherAsync();
            Color dark = Assert.IsType<SolidColorBrush>(text.Foreground).Color;
            Assert.NotEqual(light, dark);

            environment.Themes.SetTheme(UiTheme.Light);
            await WpfTestFixture.DrainDispatcherAsync();
            Assert.Equal(light, Assert.IsType<SolidColorBrush>(text.Foreground).Color);
            Assert.Equal(dictionaryCount, Application.Current.Resources.MergedDictionaries.Count);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>从实际绘制的拉丁字形确认包内设计字体，而不是只检查包含回退名单的 FontFamily。</summary>
    [Fact]
    public Task PackagedFonts_ResolveFromUiAssembly() => _fixture.RunAsync(() =>
    {
        foreach (string key in new[] { "UiFont", "MonoFont" })
        {
            var family = Assert.IsType<FontFamily>(Application.Current.FindResource(key));
            foreach (var glyphs in RenderedGlyphs(family, "Hermod OSS 12345"))
            {
                Assert.Contains("Asgard.Hermod.UI", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("Assets/Fonts", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
            }
        }
        return Task.CompletedTask;
    });

    /// <summary>中文使用打包的设计字体，不依赖开发机器的显示语言或安装字体。</summary>
    [Theory]
    [InlineData("UiFont")]
    [InlineData("MonoFont")]
    public Task PackagedFonts_UseExplicitSansSerifChineseFallback(string key) => _fixture.RunAsync(() =>
    {
        var family = Assert.IsType<FontFamily>(Application.Current.FindResource(key));
        foreach (var glyphs in RenderedGlyphs(family, "系统设置对象存储"))
        {
            Assert.Contains("Noto Sans SC", glyphs.FamilyNames.Values);
            Assert.Contains("Asgard.Hermod.UI", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
        return Task.CompletedTask;
    });

    /// <summary>强制真实排版和绘制，取得最终使用的字形字体，覆盖字体回退及打包路径。</summary>
    private static GlyphTypeface[] RenderedGlyphs(FontFamily family, string text)
    {
        var label = new TextBlock { Text = text, FontFamily = family, FontSize = 12 };
        label.Measure(new Size(500, 40));
        label.Arrange(new Rect(0, 0, 500, 40));
        label.UpdateLayout();
        new RenderTargetBitmap(500, 40, 96, 96, PixelFormats.Pbgra32).Render(label);
        var drawing = Assert.IsType<DrawingGroup>(VisualTreeHelper.GetDrawing(label));
        var glyphs = GlyphTypefaces(drawing).ToArray();
        Assert.NotEmpty(glyphs);
        return glyphs;
    }

    /// <summary>字形绘制可能嵌套在变换组中，递归读取实际 GlyphRun。</summary>
    private static IEnumerable<GlyphTypeface> GlyphTypefaces(Drawing drawing)
    {
        if (drawing is GlyphRunDrawing glyph)
            yield return glyph.GlyphRun.GlyphTypeface;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var typeface in GlyphTypefaces(child))
                    yield return typeface;
    }
}
