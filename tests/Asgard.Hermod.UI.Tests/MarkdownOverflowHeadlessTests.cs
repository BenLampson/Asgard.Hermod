using System.Globalization;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>宽代码和多列表格必须在各自区域滚动，不改变正文或外层窗口的宽度。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MarkdownOverflowHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark, 592)]
    [InlineData("zh-Hans", UiTheme.Light, 960)]
    [InlineData("en", UiTheme.Light, 1120)]
    public Task WideBlocksScrollIndependentlyAndPreserveRawSelection(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            string code = "SELECT '" + new string('x', 500) + "';";
            string header = "|" + string.Join("|", Enumerable.Range(0, 8).Select(index => $"column_{index}_original_evidence")) + "|";
            string separator = "|" + string.Join("|", Enumerable.Repeat("---", 8)) + "|";
            string values = "|" + string.Join("|", Enumerable.Range(0, 8).Select(index => $"original_value_{index}")) + "|";
            string source = $"A paragraph that follows the available body width.\n\n```sql\n{code}\n```\n\n{header}\n{separator}\n{values}";
            var view = new MarkdownView { Source = source, FontSize = 16 };
            view.Resources.MergedDictionaries.Add(Application.Current.Resources);
            view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/Asgard.Hermod.UI;component/Resources/AgentDesign.xaml", UriKind.Relative) });
            var root = new Border { Child = view, Background = (Brush)view.FindResource("SurfaceBrush") };
            using var bindings = new WpfBindingTrace(); await Layout();
            var blocks = view.Document.Blocks.OfType<BlockUIContainer>().Select(block => Assert.IsType<MarkdownOverflowBlock>(block.Child)).ToArray(); Assert.Equal(2, blocks.Length);
            var codeBlock = Assert.Single(blocks, block => block.CodeInput is not null); var input = codeBlock.CodeInput!;
            var tableBlock = Assert.Single(blocks, block => block.TableDocument is not null); var reader = Assert.IsAssignableFrom<RichTextBox>(tableBlock.Child);
            Assert.True(input.IsEnabled); Assert.True(reader.IsEnabled); Assert.True(input.IsReadOnly); Assert.True(reader.IsReadOnly);
            Assert.All(blocks, block => Assert.InRange(block.ActualWidth, width - 2, width));
            Assert.True(input.ExtentWidth > input.ViewportWidth, $"code extent={input.ExtentWidth} viewport={input.ViewportWidth}");
            Assert.True(reader.ExtentWidth > reader.ViewportWidth, $"table extent={reader.ExtentWidth} viewport={reader.ViewportWidth}");
            Assert.True(view.ExtentWidth <= view.ViewportWidth + 1, $"outer extent={view.ExtentWidth} viewport={view.ViewportWidth}");
            Capture(root, $"markdown-overflow-{language}-{theme}-{width}");
            input.ScrollToHorizontalOffset(input.ExtentWidth); reader.ScrollToHorizontalOffset(reader.ExtentWidth); await Layout();
            Assert.True(input.HorizontalOffset > 0); Assert.True(reader.HorizontalOffset > 0); Assert.Equal(0, view.HorizontalOffset);
            int selected = input.Text.IndexOf('x'); input.Select(selected, 25); Assert.Equal(new string('x', 25), input.SelectedText); Assert.Contains(code, input.Text);
            var table = Assert.Single(tableBlock.TableDocument!.Blocks.OfType<Table>()); Assert.Equal(8, table.Columns.Count);
            Assert.Contains("original_value_7", new TextRange(table.ContentStart, table.ContentEnd).Text); Assert.Equal(source, view.Source);
            Assert.Empty(bindings.Messages); Assert.Null(PresentationSource.FromVisual(view)); Assert.Null(Window.GetWindow(view));
            async Task Layout()
            {
                root.Measure(new(width, 650)); root.Arrange(new(0, 0, width, 650)); root.UpdateLayout();
                await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task ParagraphGlyphsStartAtBodyOrigin() => fixture.RunOffscreenAsync(async () =>
    {
        var view = new MarkdownView { Source = "Hello from the original body.", FontSize = 16 };
        view.Resources.MergedDictionaries.Add(Application.Current.Resources); view.Measure(new(600, 100)); view.Arrange(new(0, 0, 600, 100)); view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout(); var bitmap = new RenderTargetBitmap(600, 100, 96, 96, PixelFormats.Pbgra32); bitmap.Render(view);
        var glyph = Assert.Single(OffscreenGlyphMeasurement.Read(view, view));
        Assert.True(Math.Abs(glyph.Baseline.X) < .1, $"glyph={glyph.Baseline}; padding={view.Padding}; page={view.Document.PagePadding}; paragraph={view.Document.Blocks.FirstBlock.Margin}; viewport={view.ViewportWidth}");
    });

    private static void Capture(FrameworkElement view, string name)
    {
        if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is not { } directory) return;
        Directory.CreateDirectory(directory);
        foreach (int scale in new[] { 1, 2 })
        {
            var bitmap = new RenderTargetBitmap((int)view.ActualWidth * scale, (int)view.ActualHeight * scale, 96 * scale, 96 * scale, PixelFormats.Pbgra32); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, $"{name}-{scale * 100}pct.png")); encoder.Save(stream);
        }
    }
}
