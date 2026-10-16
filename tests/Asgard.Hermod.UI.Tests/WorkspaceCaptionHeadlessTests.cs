using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>以离屏排版验证当前 Pencil 标题规格与实际字体，不创建 HWND 或操作桌面。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceCaptionHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Light, 1120, 96)]
    [InlineData("zh-Hans", UiTheme.Dark, 1600, 96)]
    [InlineData("zh-Hans", UiTheme.Light, 800, 192)]
    public Task Caption_UsesSingleLineMetricsAndPackagedGlyphs(string language, UiTheme theme, double width, double dpi) => fixture.RunOffscreenAsync(() =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var instance = new InstanceItemViewModel(new("caption", "Production 数据库", "Database", "database", [], TypeId: "postgresql"));
            var identity = new WorkspaceIdentity { Instance = instance, Endpoint = "database.prod.internal:5432", Icon = "brand-postgresql" };
            var badge = new StatusBadge { Text = Text.Get("Production"), IsCaption = true, ShowIndicator = false, Tone = StatusTone.Warning };
            var action = new Button { Content = Text.Get("DatabaseSessionAgent"), Height = 28, MinHeight = 28, FontSize = 12, Padding = new(9, 0, 9, 0) };
            var header = new WorkspaceHeader { IdentityContent = identity, StatusContent = badge, ActionsContent = action, ShowWindowControls = true };
            var root = new Border { Child = header };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Measure(new(width, 44)); root.Arrange(new(0, 0, width, 44)); root.UpdateLayout();
            Assert.Equal(44, header.ActualHeight);
            Assert.Equal(28, action.ActualHeight);
            Assert.Equal(18, badge.ActualHeight);
            var name = (TextBlock)identity.FindName("InstanceName"); var address = (TextBlock)identity.FindName("Address");
            Assert.Equal(13, name.FontSize); Assert.Equal(11, address.FontSize);
            Assert.Equal(width < 1000 ? Visibility.Collapsed : Visibility.Visible, address.Visibility);
            Assert.Empty(WindowTestVisuals.Descendants<InstanceNameButton>(identity));
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            var image = new RenderTargetBitmap((int)(width * dpi / 96), (int)(44 * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
            image.Render(root);
            var family = (FontFamily)Application.Current.FindResource("WorkspaceFont");
            foreach (var weight in new[] { FontWeights.Normal, FontWeights.SemiBold })
            {
                Assert.True(new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal).TryGetGlyphTypeface(out var glyphs));
                Assert.Contains("Noto Sans SC", glyphs.FamilyNames.Values);
                Assert.Contains("Asgard.Hermod.UI", glyphs.FontUri.OriginalString, StringComparison.OrdinalIgnoreCase);
                Assert.True(glyphs.CharacterToGlyphMap.ContainsKey('数'));
            }
            string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                using var stream = File.Create(Path.Combine(directory, $"caption-{language}-{theme}-{width}-{dpi}.png")); encoder.Save(stream);
            }
        }
        finally { Text.SetApplicationCulture(null); }
        return Task.CompletedTask;
    });
}
