using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql.UI.ViewModels;
using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.UI.Tests;

/// <summary>执行记录与真实滚动字形的验证只构造纯控件，不连接服务器或创建窗口。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseHistoryHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light)]
    [InlineData("en", UiTheme.Dark)]
    public Task HistoryHasReferenceRowsAndIndependentSnapshotTabs(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            await using var model = new MySqlTabViewModel("Query", "app", Asgard.Hermod.MySql.MySqlObjectKind.Root, null!, null!, null!);
            var first = new MySqlExecutionTrace(DateTimeOffset.UtcNow, "explain", "app", "SELECT original;", TimeSpan.FromMilliseconds(25.5), 1, true, "Immutable result evidence");
            model.ExecutionTraces.Add(first);
            model.ExecutionTraces.Add(new(DateTimeOffset.UtcNow.AddMinutes(-1), "query", "app", "SELECT failed;", TimeSpan.FromMilliseconds(15), 0, false, "Safe failure"));
            model.SelectedTrace = first;
            var view = new DatabaseExecutionHistory { DataContext = model }; using var bindings = new WpfBindingTrace();
            await Layout(view, 1362, 462);
            var list = (ListBox)view.FindName("TraceList"); var rows = WindowTestVisuals.Descendants<ListBoxItem>(list).ToArray();
            Assert.Equal(2, rows.Length); Assert.All(rows, row => Assert.Equal(62, row.ActualHeight));
            Assert.Equal(360, list.ActualWidth); Assert.Equal(new Point(0, 42), list.TranslatePoint(new(), view));
            var sql = (TextBox)view.FindName("SqlSnapshot"); var details = (TextBox)view.FindName("ReturnedDetails");
            Assert.Equal(new Point(360, 80), sql.TranslatePoint(new(), view)); Assert.Equal(first.Sql, sql.Text);
            Capture(view, $"db-history-{language}-{theme}");
            var returned = WindowTestVisuals.Descendants<RadioButton>(view).Single(button => button.Content as string == Text.Get("DatabaseTraceReturnedDetails"));
            returned.IsChecked = true; await Layout(view, 1362, 462);
            Assert.Equal(Visibility.Collapsed, sql.Visibility); Assert.Equal(Visibility.Visible, details.Visibility); Assert.Equal(first.Detail, details.Text);
            ((ComboBox)view.FindName("KindFilter")).SelectedValue = "plan"; await Layout(view, 1362, 462);
            Assert.Same(first, Assert.Single(list.Items.Cast<object>())); Assert.Same(first, model.SelectedTrace);
            model.Sql = "SELECT changed_draft;"; await Layout(view, 1362, 462);
            Assert.Equal(2, model.ExecutionTraces.Count); Assert.Equal("SELECT original;", sql.Text);
            Assert.Empty(bindings.Messages); Assert.Null(PresentationSource.FromVisual(view));
            // 两个协议模块均提供相同的展示契约，且不会借用另一个执行的时间或目标。
            var postgres = new PostgreSqlExecutionTrace(first.Started, first.Kind, first.Database, first.Sql, first.Elapsed, first.Rows, first.Succeeded, first.Detail);
            Assert.Equal(first.TimeLabel, postgres.TimeLabel); Assert.Equal(first.Metrics, postgres.Metrics); Assert.Equal(first.KindLabel, postgres.KindLabel);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task SqlGutterTracksActualCharacterRowsAfterPixelScrolling() => fixture.RunOffscreenAsync(async () =>
    {
        var editor = new CodeEditor { IsSqlWorkspace = true, IsReadOnly = false, Text = string.Join("\r\n", Enumerable.Range(1, 60).Select(index => $"SELECT {index};")) };
        await Layout(editor, 720, 180);
        var input = editor.TextInputControl; var numbers = (TextBlock)editor.FindName("LineNumbers");
        input.ScrollToVerticalOffset(24 * 35 + 7); await Layout(editor, 720, 180);
        var bitmap = new RenderTargetBitmap(720, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(editor);
        string[] labels = numbers.Text.Split(Environment.NewLine);
        var code = OffscreenGlyphMeasurement.Read(input, editor); var gutter = OffscreenGlyphMeasurement.Read(numbers, editor);
        Assert.NotEmpty(code); Assert.NotEmpty(gutter); Assert.True(code.Count == gutter.Count, $"viewport={input.ViewportHeight}, actual={input.ActualHeight}, offset={input.VerticalOffset}, code={string.Join(",", code.Select(g => g.Baseline.Y))}, gutter={string.Join(",", gutter.Select(g => g.Baseline.Y))}");
        for (int index = 0; index < code.Count; index++) Assert.InRange(Math.Abs(code[index].Baseline.Y - gutter[index].Baseline.Y), 0, .05);
        int first = input.GetCharacterIndexFromPoint(new(0, input.Padding.Top), true);
        int physicalNumber = input.Text.AsSpan(0, first).Count('\n') + 1;
        Assert.Equal(physicalNumber.ToString(CultureInfo.InvariantCulture), labels[0]); Assert.InRange(physicalNumber, 30, 40);
        Assert.Equal(60, editor.Text.Split('\n').Length); Assert.Null(PresentationSource.FromVisual(editor));
    });

    private static async Task Layout(FrameworkElement view, double width, double height)
    {
        view.Resources.MergedDictionaries.Add(Application.Current.Resources);
        view.Measure(new(width, height)); view.Arrange(new(0, 0, width, height)); view.UpdateLayout();
        await WpfTestFixture.DrainDispatcherAsync(); view.UpdateLayout();
    }
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
