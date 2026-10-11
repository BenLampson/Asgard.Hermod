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

    /// <summary>两套设计字体必须从 UI 程序集实际解析，不能静默回退到系统字体。</summary>
    [Fact]
    public Task PackagedFonts_ResolveFromUiAssembly() => _fixture.RunAsync(() =>
    {
        foreach (string key in new[] { "UiFont", "MonoFont" })
        {
            var family = Assert.IsType<FontFamily>(Application.Current.FindResource(key));
            var typeface = new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out var glyphs), $"无法加载 {key} 的字形。");
            Assert.Contains("Asgard.Hermod.UI", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Assets/Fonts", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
        }
        return Task.CompletedTask;
    });
}
