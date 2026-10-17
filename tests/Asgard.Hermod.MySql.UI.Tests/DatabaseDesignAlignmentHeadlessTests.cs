using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>与当前 DBWindow/oMvco 使用相同逻辑画板和语言，验证真正的视图边界。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class DatabaseDesignAlignmentHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData(1600, 1000, 1)]
    [InlineData(3840, 2160, 1)]
    [InlineData(1920, 1080, 2)]
    public Task SqlWorkspace_MatchesReferenceGeometryAt1600By1000(double width, double height, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("zh-Hans"));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light);
            var config = new Configuration(); var server = CreateServer();
            await using var page = new MySqlWorkspaceViewModel(new(Configuration.Instance), config, server, config, new TestMySqlDialogs(), Database);
            await page.InitializeAsync(); var script = page.ActiveSession!;
            page.NewQuery(Database); page.ActiveTab = script;
            var database = Assert.Single(page.SessionTreeItems); await page.Tree!.LoadAsync(database); database.IsExpanded = true;
            var tables = database.Children.Single(node => node.Object.Kind == MySqlObjectKind.Tables);
            await page.Tree.LoadAsync(tables); tables.IsExpanded = true;
            await page.OpenObjectAsync(tables.Children.Single(node => node.Object.Name == "model_config").Object); page.ActiveTab = script;
            script.Sql = "SELECT id, name, provider, model, enabled, updated_at\r\nFROM model_config\r\nWHERE enabled = 1\r\nORDER BY updated_at DESC;";
            await script.ExecuteAsync(script.Sql);
            using var bindings = new WpfBindingTrace();
            var view = new MySqlWorkspaceView { DataContext = page };
            var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout");
            var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar); layout.Toolbar = null; header.DataContext = page; header.ShowWindowControls = true;
            var root = new DockPanel(); root.Resources.MergedDictionaries.Add(Application.Current.Resources);
            DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(view);
            root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout();
            await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout(); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); root.UpdateLayout();
            var selectedObject = tables.Children.Single(node => node.Object.Name == "model_config");
            var treeItem = WindowTestVisuals.Descendants<TreeViewItem>(view).Single(item => ReferenceEquals(item.DataContext, selectedObject));
            treeItem.IsSelected = true; page.ActiveTab = script; root.UpdateLayout();
            Assert.True(treeItem.IsSelected);
            var selectedRow = Assert.IsType<Border>(treeItem.Template.FindName("Row", treeItem));
            Assert.Equal(12, selectedRow.TranslatePoint(new(), root).X); Assert.Equal(208, selectedRow.ActualWidth);
            var tab = Assert.IsType<MySqlTabView>(((ContentControl)view.FindName("TabHost")).Content);
            var sql = Assert.IsType<DatabaseSqlWorkspace>(tab.FindName("SqlWorkspace"));
            // 列宽与选中项是用户可以保存的视图状态，截图夹具恢复参考画板中的该状态。
            var grid = Assert.Single(WindowTestVisuals.Descendants<DataGrid>(Assert.IsType<MySqlResultView>(sql.ResultContent)));
            double[] widths = [72, 220, 160, 256, 100];
            for (int i = 0; i < widths.Length; i++) grid.Columns[i].Width = new(widths[i]);
            grid.Columns[^1].Width = new(1, DataGridLengthUnitType.Star); grid.SelectedItems.Add(script.SelectedResult!.Rows[0]);
            root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            Assert.Equal(new Point(238, 116), sql.TranslatePoint(new(), root));
            Assert.Equal(width - 238, sql.ActualWidth);
            Assert.Equal(new Point(238, 164), sql.Editor.TranslatePoint(new(), root));
            Assert.Equal(268, sql.Editor.ActualHeight);
            Assert.Equal(232, ((Border)view.FindName("TreePanel")).ActualWidth);
            var run = Assert.IsType<Button>(sql.FindName("RunButton"));
            Assert.Equal(Color.FromRgb(0x30, 0x6B, 0xEA), Assert.IsType<SolidColorBrush>(run.Background).Color);
            var closeIcons = WindowTestVisuals.Descendants<Button>(view).Where(button => button.Visibility == Visibility.Visible && button.ActualWidth == 12 && button.Content is IconView { Kind: "x" }).ToArray();
            Assert.NotEmpty(closeIcons);
            Assert.All(closeIcons, close => { Assert.Equal(new Thickness(0), close.Padding); Assert.Equal(12, Assert.IsType<IconView>(close.Content).ActualWidth); });
            Assert.Equal(5, script.SelectedResult!.Rows.Count);
            // 夹具刻意没有 Window，原生窗口按钮的 Window 祖先绑定因此没有目标；正文的绑定错误仍必须失败。
            var unexpectedBindings = bindings.Messages.Where(message => !message.StartsWith("System.Windows.Data Error: 4 :", StringComparison.Ordinal)
                && !message.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal)).ToArray();
            Assert.True(unexpectedBindings.Length == 0, string.Join(Environment.NewLine, unexpectedBindings));
            Assert.Null(PresentationSource.FromVisual(root)); Assert.Null(Window.GetWindow(root));
            var measured = new RenderTargetBitmap((int)(width * dpi), (int)(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); measured.Render(root);
            var codeGlyphs = OffscreenGlyphMeasurement.Read((TextBox)sql.Editor.FindName("Input"), root);
            var numberGlyphs = OffscreenGlyphMeasurement.Read((TextBlock)sql.Editor.FindName("LineNumbers"), root);
            Assert.Equal(4, codeGlyphs.Count); Assert.Equal(4, numberGlyphs.Count);
            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(13, codeGlyphs[i].Size); Assert.Equal(11, numberGlyphs[i].Size);
                Assert.InRange(Math.Abs(codeGlyphs[i].Baseline.Y - numberGlyphs[i].Baseline.Y), 0, 0.01);
                Assert.Equal(294, codeGlyphs[i].Baseline.X);
            }
            string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)(width * dpi), (int)(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); bitmap.Render(root);
                var input = (TextBox)sql.Editor.FindName("Input"); var numbers = (TextBlock)sql.Editor.FindName("LineNumbers");
                File.WriteAllText(Path.Combine(directory, "sql-layout.txt"), $"rect={input.GetRectFromCharacterIndex(0)} numbersBaseline={numbers.BaselineOffset} inputOrigin={input.TranslatePoint(new(), root)} numbersOrigin={numbers.TranslatePoint(new(), root)} fontBaseline={input.FontFamily.Baseline} spacing={input.FontFamily.LineSpacing}");
                File.WriteAllLines(Path.Combine(directory, "sql-glyphs.txt"),
                    OffscreenGlyphMeasurement.Read(input, root).Select(g => "code " + g).Concat(OffscreenGlyphMeasurement.Read(numbers, root).Select(g => "number " + g)));

                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var stream = File.Create(Path.Combine(directory, $"mysql-oMvco-zh-Hans-Light-{width}x{height}-{dpi * 100}pct.png")); encoder.Save(stream);
            }
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, true)]
    [InlineData("en", UiTheme.Dark, false)]
    public Task PlanReader_ReusesQueryOutputSlotAndPreservesResultSnapshot(string language, UiTheme theme, bool analyze) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            new ThemeService(Application.Current.Resources).SetTheme(theme);
            var config = new Configuration(); var server = CreateServer(); var dialogs = new TestMySqlDialogs { AnalyzeAllowed = true };
            await using var page = new MySqlWorkspaceViewModel(new(Configuration.Instance), config, server, config, dialogs, Database);
            await page.InitializeAsync(); var script = page.ActiveSession!; page.NewQuery(Database); page.ActiveTab = script;
            var database = Assert.Single(page.SessionTreeItems); await page.Tree!.LoadAsync(database); database.IsExpanded = true;
            var tables = database.Children.Single(node => node.Object.Kind == MySqlObjectKind.Tables); await page.Tree.LoadAsync(tables); tables.IsExpanded = true;
            await page.OpenObjectAsync(tables.Children.Single(node => node.Object.Name == "model_config").Object); page.ActiveTab = script;
            script.Sql = "SELECT id, name, provider, model, enabled, updated_at\r\nFROM model_config\r\nWHERE enabled = 1\r\nORDER BY updated_at DESC;";
            await script.ExecuteAsync(script.Sql); var result = script.SelectedResult; string queryTime = script.QuerySummary;
            const string raw = "-> Sort: model_config.updated_at DESC\n   (actual time=0.221..0.241 rows=5 loops=1)\n   -> Filter: (model_config.enabled = 1)\n      (actual time=0.061..0.118 rows=5 loops=1)\n      -> Table scan on model_config\n         (actual time=0.052..0.083 rows=20 loops=1)";
            server.ResultOverride = analyze ? new(1, 1, [new(0, "EXPLAIN", "TEXT", typeof(string), false)], [[raw]]) :
                new(1, 1, new[] { "id", "select_type", "table", "type", "possible_keys", "key", "rows", "Extra" }.Select((name, index) => new MySqlColumn(index, name, "VARCHAR", typeof(string), true)).ToArray(),
                    [["1", "SIMPLE", "model_config", "ALL", "idx_enabled_updated", null, "100", "Using where; Using filesort"]]);
            server.Elapsed = TimeSpan.FromMilliseconds(26); await script.ExplainScopeAsync("", 0, analyze);
            Assert.Equal("plan", script.OutputView); Assert.NotNull(script.Plan); Assert.Same(result, script.SelectedResult); Assert.Equal(queryTime, script.QuerySummary);
            Assert.Equal(script.Plan.Snapshot, script.SelectedTrace!.Plan);
            if (analyze) script.Plan.SelectedNode = script.Plan.VisibleNodes[^1];
            using var bindings = new WpfBindingTrace(); var view = new MySqlWorkspaceView { DataContext = page };
            var layout = (WorkspacePageLayout)view.FindName("WorkspaceLayout"); var header = Assert.IsType<WorkspaceHeader>(layout.Toolbar);
            layout.Toolbar = null; header.DataContext = page; header.ShowWindowControls = true;
            var root = new DockPanel(); root.Resources.MergedDictionaries.Add(Application.Current.Resources); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header); root.Children.Add(view);
            root.Measure(new(1600, 1000)); root.Arrange(new(0, 0, 1600, 1000)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); root.UpdateLayout();
            var tab = Assert.IsType<MySqlTabView>(((ContentControl)view.FindName("TabHost")).Content);
            var sql = Assert.IsType<DatabaseSqlWorkspace>(tab.FindName("SqlWorkspace")); var reader = Assert.IsType<DatabasePlanReader>(sql.FindName("PlanReader"));
            Assert.Equal(Visibility.Visible, reader.Visibility); Assert.Equal(new Point(238, 478), reader.TranslatePoint(new(), root)); Assert.Equal(462, reader.ActualHeight);
            Assert.Same(script.Plan, reader.DataContext); Assert.Equal(296, ((Border)reader.FindName("DetailsPanel")).ActualWidth);
            int calls = server.Executions; script.Plan.ViewMode = "raw"; root.UpdateLayout();
            Assert.Equal(script.PlanRawText, ((TextBox)reader.FindName("RawEditor")).Text); script.Plan.ViewMode = "structured"; root.UpdateLayout(); Assert.Equal(calls, server.Executions);
            var unexpected = bindings.Messages.Where(message => !message.StartsWith("System.Windows.Data Error: 4 :", StringComparison.Ordinal) && !message.Contains("AncestorType='System.Windows.Window'", StringComparison.Ordinal)).ToArray();
            Assert.True(unexpected.Length == 0, string.Join(Environment.NewLine, unexpected));
            string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT");
            if (directory is not null)
            {
                Directory.CreateDirectory(directory); var bitmap = new RenderTargetBitmap(1600, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(Path.Combine(directory, $"mysql-plan-{language}-{theme}-{analyze}-1600x1000.png")); encoder.Save(stream);
            }
            Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private const string Database = "ben-aggregation-platform";
    private static TestMySql CreateServer() => new()
    {
        DatabaseNames = [Database],
        ResultOverride = ExampleResult(),
        Version = "8.4",
        Elapsed = TimeSpan.FromMilliseconds(25.7),
        MetadataOverride = parent => parent.Kind switch
        {
            MySqlObjectKind.Database => [new(MySqlObjectKind.Tables, "", Database), new(MySqlObjectKind.Views, "", Database),
                new(MySqlObjectKind.Procedures, "", Database), new(MySqlObjectKind.Functions, "", Database), new(MySqlObjectKind.Events, "", Database)],
            MySqlObjectKind.Tables => new[] { "audit_record", "event_log", "model_config", "user_account", "user_session" }.Select(name => new MySqlObject(MySqlObjectKind.Table, name, Database)).ToArray(),
            _ => null
        }
    };
    private static MySqlResult ExampleResult()
    {
        MySqlColumn[] columns = [new(0, "id", "BIGINT", typeof(long), false), new(1, "name", "VARCHAR", typeof(string), false),
            new(2, "provider", "VARCHAR", typeof(string), false), new(3, "model", "VARCHAR", typeof(string), false),
            new(4, "enabled", "BOOLEAN", typeof(bool), false), new(5, "updated_at", "DATETIME", typeof(DateTime), false)];
        return new(1, 1, columns, [
            [1L, "公司模型", "OpenAI", "gpt-4.1", true, new DateTime(2026, 10, 16, 9, 41, 12)],
            [2L, "个人模型", "Anthropic", "claude-sonnet-4", true, new DateTime(2026, 10, 16, 9, 35, 8)],
            [3L, "本地模型", "Ollama", "qwen3:14b", true, new DateTime(2026, 10, 15, 18, 20, 49)],
            [4L, "测试模型", "OpenAI", "gpt-4.1-mini", true, new DateTime(2026, 10, 14, 10, 8, 16)],
            [5L, "备用模型", "OpenAI", "gpt-4.1", true, new DateTime(2026, 10, 13, 16, 25, 0)]]);
    }
    private sealed class Configuration : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("mysql", "公司99服务器", "公司99服务器", "brand-mysql", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct) { TagIds = ["builtin.environment.development"] };
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Direct, Parameters = new Dictionary<string, string> { ["host"] = "10.100.11.99", ["user"] = "test", ["database"] = Database } });
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
