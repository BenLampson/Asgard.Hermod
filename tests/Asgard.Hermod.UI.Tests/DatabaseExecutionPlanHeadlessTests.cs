using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实计划阅读器的离屏几何、模式、数字与选择回归，不创建 Window 或连接。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseExecutionPlanHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1362)]
    [InlineData("en", UiTheme.Dark, 1362)]
    [InlineData("zh-Hans", UiTheme.Dark, 800)]
    public Task TreeReader_UsesReferenceMetricsAndPreservesSelectionAcrossModes(string language, UiTheme theme, double width) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var model = new DatabaseExecutionPlanViewModel(ActualPlan());
            model.SelectedNode = model.VisibleNodes[^1]; var selected = model.SelectedNode;
            using var bindings = new WpfBindingTrace();
            var view = new DatabasePlanReader { DataContext = model }; var root = Root(view); Layout(root, width);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, width);
            var body = (Grid)view.FindName("ReaderBody"); var details = (Border)view.FindName("DetailsPanel");
            var rows = (ListBox)view.FindName("NodeRows");
            Assert.Equal(new Point(0, 40), body.TranslatePoint(new(), root)); Assert.Equal(422, body.ActualHeight);
            Assert.Equal(new Point(0, 72), rows.TranslatePoint(new(), root)); Assert.Equal(204, rows.ActualHeight);
            if (width == 1362)
            {
                Assert.Equal(new Point(1066, 40), details.TranslatePoint(new(), root)); Assert.Equal(296, details.ActualWidth);
                var access = WindowTestVisuals.Descendants<TextBlock>(details).Single(text => text.Text == Text.Get("PlanAccessObject"));
                var value = WindowTestVisuals.Descendants<TextBlock>(details).Single(text => text.Text == "model_config");
                Assert.Equal(1084, access.TranslatePoint(new(), root).X); Assert.Equal(1184, value.TranslatePoint(new(), root).X);
                Assert.InRange(access.TranslatePoint(new(), root).Y, 149, 151);
            }
            else { Assert.False(model.IsDetailsOpen); Assert.Equal(0, ((ColumnDefinition)view.FindName("DetailsColumn")).ActualWidth); }
            Assert.Equal("0.052 / 0.083", selected!.ActualTime); Assert.Equal("20", selected.ActualRows); Assert.Equal("1", selected.Loops);
            Assert.Equal("0.083 ms", model.Details.Single(item => item.Name == Text.Get("PlanFinishTime")).Value);
            Capture(root, $"plan-tree-{language}-{theme}-{width}");
            model.ViewMode = "raw"; Layout(root, width);
            var raw = (TextBox)view.FindName("RawEditor"); Assert.True(raw.IsReadOnly); Assert.Equal(model.Snapshot.RawText, raw.Text);
            Assert.False(model.IsDetailsVisible); Assert.Equal(0, ((ColumnDefinition)view.FindName("DetailsColumn")).ActualWidth);
            Capture(root, $"plan-raw-{language}-{theme}-{width}");
            model.ViewMode = "structured"; Layout(root, width); Assert.Same(selected, model.SelectedNode);
            model.CollapseAllCommand.Execute(null); Assert.Single(model.VisibleNodes); Assert.Same(model.VisibleNodes[0], model.SelectedNode);
            model.ExpandAllCommand.Execute(null); Assert.Equal(3, model.VisibleNodes.Count);
            Assert.Equal(model.Snapshot.RawText, model.RawText); Assert.Empty(bindings.Messages);
            Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task TraditionalFields_KeepAllFieldsAndLocateOnlyTheOriginalRow() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light);
            const string raw = "id\tselect_type\ttable\ttype\tpossible_keys\tkey\trows\tExtra\r\n1\tSIMPLE\tmodel_config\tALL\tidx_enabled\tNULL\t100\tUsing where; Using filesort\r\n";
            var snapshot = new DatabaseExecutionPlan
            {
                RawText = raw,
                Sql = "SELECT * FROM model_config",
                Database = "app",
                Format = DatabasePlanFormat.Fields,
                Columns = ["id", "select_type", "table", "type", "possible_keys", "key", "rows", "Extra"],
                Rows = [new string?[] { "1", "SIMPLE", "model_config", "ALL", "idx_enabled", null, "100", "Using where; Using filesort" }]
            };
            var model = new DatabaseExecutionPlanViewModel(snapshot); using var bindings = new WpfBindingTrace();
            var view = new DatabasePlanReader { DataContext = model }; var root = Root(view); Layout(root, 1362);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 1362);
            Assert.True(model.IsFieldSummary); Assert.Empty(model.VisibleNodes); Assert.True(model.IsDetailsVisible);
            Assert.Equal("100", model.SelectedField!.EstimatedRows); Assert.Equal("ALL", model.SelectedField.Access);
            Assert.Equal(Text.Get("PlanMissingMetric"), model.SelectedField.UsedKey); Assert.Equal(8, model.RawFields.Count);
            Capture(root, "plan-fields-en-Light-1362");
            int located = -1, length = -1; model.LocateRequested += (start, count) => { located = start; length = count; };
            model.LocateRawCommand.Execute(null);
            Assert.Equal(raw.IndexOf("1\tSIMPLE", StringComparison.Ordinal), located); Assert.EndsWith("Using filesort", raw.Substring(located, length));
            Assert.Equal("raw", model.ViewMode); Assert.Equal(raw, model.RawText); Assert.Empty(bindings.Messages);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 1362); Capture(root, "plan-fields-raw-en-Light-1362");
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task UnknownFormat_KeepsLargeRawAndNeverEnablesInventedStructure() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        try
        {
            var model = new DatabaseExecutionPlanViewModel(new() { RawText = new string('数', 200000), Sql = "SELECT 1", Database = "app", IsActual = true, RequestSucceeded = false });
            var view = new DatabasePlanReader { DataContext = model }; var root = Root(view); Layout(root, 800);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 800);
            model.ViewMode = "structured"; Assert.Equal("raw", model.ViewMode); Assert.False(model.Modes[0].IsEnabled);
            Assert.Equal(200000, ((TextBox)view.FindName("RawEditor")).Text.Length);
            Assert.Equal(Text.Get("PlanFailedFact"), model.Fact); Assert.False(model.IsDetailsVisible);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task EstimatedTree_ShowsCostsAndWidthWithoutMeasuredTime() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        try
        {
            var snapshot = new DatabaseExecutionPlan
            {
                RawText = "{\"Plan\":{\"Node Type\":\"Seq Scan\"}}",
                Sql = "SELECT * FROM model_config",
                Database = "app",
                Format = DatabasePlanFormat.Tree,
                Roots = [new() { Id = "0", Operation = DatabasePlanOperation.SequentialScan, ServerOperator = "Seq Scan", FullOperation = "Seq Scan", Expression = "model_config",
                    Metrics = new() { StartupCost = "0.00", TotalCost = "1.25", EstimatedRows = "5", EstimatedWidth = "80" } }]
            };
            var model = new DatabaseExecutionPlanViewModel(snapshot); var view = new DatabasePlanReader { DataContext = model }; var root = Root(view); Layout(root, 1362);
            await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 1362);
            Assert.Equal(Text.Get("PlanEstimatedRows"), model.RowsHeading); Assert.Equal(Text.Get("PlanCostHeading"), model.MetricHeading);
            Assert.Equal("1.25", model.SelectedNode!.Cost); Assert.Equal("80", model.SelectedNode.EstimatedWidth);
            Assert.DoesNotContain(model.Details, item => item.Name == Text.Get("PlanFirstTime") || item.Name == Text.Get("PlanFinishTime"));
            Assert.Equal(Text.Get("PlanCostNotice"), model.MetricNotice); Assert.Equal(Text.Get("PlanOperatorSequentialScan"), model.SelectedTitle);
            Capture(root, "plan-postgresql-estimate-zh-Hans-1362");
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task RawSearch_NextAndPreviousWrapWithoutChangingEvidence() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            var model = new DatabaseExecutionPlanViewModel(new() { RawText = "marker marker", Sql = "SELECT 1", Database = "app" }) { SearchText = "marker" };
            var view = new DatabasePlanReader { DataContext = model }; var root = Root(view); Layout(root, 800); await WpfTestFixture.DrainDispatcherAsync(); Layout(root, 800);
            var raw = (TextBox)view.FindName("RawEditor"); raw.Select(0, 0);
            var buttons = WindowTestVisuals.Descendants<Button>(view).ToArray();
            var next = buttons.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == Text.Get("PlanSearchNext"));
            var previous = buttons.Single(button => System.Windows.Automation.AutomationProperties.GetName(button) == Text.Get("PlanSearchPrevious"));
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, raw.SelectionStart); Assert.Equal(6, raw.SelectionLength);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(7, raw.SelectionStart);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(0, raw.SelectionStart);
            previous.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Assert.Equal(7, raw.SelectionStart);
            Assert.Equal("marker marker", model.RawText); Assert.Null(Window.GetWindow(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private static DatabaseExecutionPlan ActualPlan()
    {
        const string raw = "-> Sort: model_config.updated_at DESC\n   (actual time=0.221..0.241 rows=5 loops=1)\n   -> Filter: (model_config.enabled = 1)\n      (actual time=0.061..0.118 rows=5 loops=1)\n      -> Table scan on model_config\n         (actual time=0.052..0.083 rows=20 loops=1)";
        var scan = new DatabasePlanNode
        {
            Id = "0/0/0",
            Operation = DatabasePlanOperation.TableScan,
            ServerOperator = "Table scan",
            FullOperation = "Table scan on model_config",
            Expression = "model_config",
            Metrics = new() { ActualRows = "20", FirstRowMilliseconds = "0.052", CompletionMilliseconds = "0.083", Loops = "1" },
            SourceOffset = raw.IndexOf("-> Table scan", StringComparison.Ordinal),
            SourceLength = raw.Length - raw.IndexOf("-> Table scan", StringComparison.Ordinal)
        };
        var filter = new DatabasePlanNode
        {
            Id = "0/0",
            Operation = DatabasePlanOperation.Filter,
            ServerOperator = "Filter",
            FullOperation = "Filter: (model_config.enabled = 1)",
            Expression = "model_config.enabled = 1",
            Children = [scan],
            Metrics = new() { ActualRows = "5", FirstRowMilliseconds = "0.061", CompletionMilliseconds = "0.118", Loops = "1" }
        };
        return new()
        {
            RawText = raw,
            Sql = "SELECT * FROM model_config WHERE enabled = 1 ORDER BY updated_at DESC",
            Database = "app",
            IsActual = true,
            RequestElapsed = TimeSpan.FromMilliseconds(26),
            Format = DatabasePlanFormat.Tree,
            Roots = [new() { Id = "0", Operation = DatabasePlanOperation.Sort, ServerOperator = "Sort", FullOperation = "Sort: model_config.updated_at DESC", Expression = "model_config.updated_at DESC", Children = [filter],
                Metrics = new() { ActualRows = "5", FirstRowMilliseconds = "0.221", CompletionMilliseconds = "0.241", Loops = "1" } }]
        };
    }
    private static Border Root(FrameworkElement content) { var root = new Border { Child = content }; root.Resources.MergedDictionaries.Add(Application.Current.Resources); return root; }
    private static void Layout(FrameworkElement root, double width) { root.Measure(new(width, 462)); root.Arrange(new(0, 0, width, 462)); root.UpdateLayout(); }
    private static void Capture(FrameworkElement root, string name)
    {
        string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return;
        Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
    }
}
