using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.Tests;

/// <summary>展开证据使用原调用的 SQL、目标与有界结果，只读按钮不会读取、写入或创建窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseAgentEvidenceHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark, 592, 1)]
    [InlineData("zh-Hans", UiTheme.Light, 960, 1)]
    [InlineData("en", UiTheme.Light, 1120, 2)]
    [InlineData("zh-Hans", UiTheme.Dark, 1120, 2)]
    public Task ExactSqlAndFrozenResultUseSeparateReadOnlyViewports(string language, UiTheme theme, double width, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            string sql = "SELECT '<Button Click=Execute>literal</Button>', " + new string('x', 600) + ";\nSELECT 2;";
            var evidence = new DatabaseAgentEvidenceViewModel(sql, "fixed instance / Session 1 / app / host:3306");
            var view = new AgentMessageView { IsTool = true, IsExpanded = true, ToolEvidence = evidence, Body = "legacy combined body must not be duplicated" };
            var root = new Border { Child = view }; root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            root.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Asgard.Hermod.UI;component/Resources/AgentDesign.xaml") });
            root.SetResourceReference(Border.BackgroundProperty, "SurfaceBrush"); using var trace = new WpfBindingTrace(); await Layout();
            var card = Assert.Single(WindowTestVisuals.Descendants<DatabaseAgentEvidenceView>(view)); var code = (TextBox)card.FindName("SqlEvidence");
            Assert.Equal(sql, code.Text); Assert.True(code.IsReadOnly); Assert.Equal(12, code.FontSize); Assert.False(((Button)card.FindName("ViewResult")).IsEnabled);
            Assert.True(double.IsPositiveInfinity(code.MaxHeight)); Assert.Equal(ScrollBarVisibility.Disabled, code.VerticalScrollBarVisibility);
            const string output = "{\"columns\":[\"name\"],\"rows\":[[\"literal <Hyperlink>data</Hyperlink>\"]],\"sample\":true}";
            evidence.Complete(new("completed", output) { Metrics = new(3, 2, TimeSpan.FromMilliseconds(800), false) }); await Layout();
            Assert.True(((Button)card.FindName("ViewResult")).IsEnabled); Assert.Equal(Text.Format("ChatToolQueryMetrics", 3, 2, 800d), evidence.Summary);
            Assert.Equal(Visibility.Collapsed, ((StackPanel)card.FindName("ResultEvidence")).Visibility);
            ((Button)card.FindName("ViewResult")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); Assert.True(evidence.IsResultExpanded);
            var result = Assert.Single(WindowTestVisuals.Descendants<TextBox>(card), input => input.Text == output); Assert.True(result.IsReadOnly); Assert.Equal(output, result.Text);
            Assert.True(double.IsPositiveInfinity(result.MaxHeight)); Assert.Equal(ScrollBarVisibility.Disabled, result.VerticalScrollBarVisibility);
            ((Button)card.FindName("ViewResult")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); Assert.False(evidence.IsResultExpanded);
            ((Button)card.FindName("ViewResult")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Layout(); Assert.True(evidence.IsResultExpanded);
            Assert.All(WindowTestVisuals.Descendants<MarkdownView>(view), markdown => Assert.Equal(Visibility.Collapsed, markdown.Visibility));
            Assert.True(code.ExtentWidth > code.ViewportWidth); Assert.Equal(30, ((Button)card.FindName("CopySql")).ActualHeight);
            evidence.Complete(new("completed", "late replacement")); Assert.Equal(output, evidence.Output); Assert.Equal(sql, evidence.Sql);
            Assert.Empty(trace.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            if (Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT") is { } directory)
            {
                Directory.CreateDirectory(directory); var image = new RenderTargetBitmap((int)(width * dpi), (int)(700 * dpi), dpi * 96, dpi * 96, PixelFormats.Pbgra32); image.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = File.Create(Path.Combine(directory, $"agent-sql-evidence-{language}-{theme}-{width}-{dpi * 100}pct.png")); encoder.Save(stream);
            }
            async Task Layout()
            {
                for (int pass = 0; pass < 3; pass++) { root.Measure(new(width, 700)); root.Arrange(new(0, 0, width, 700)); root.UpdateLayout(); VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); await WpfTestFixture.DrainDispatcherAsync(); }
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });
}
