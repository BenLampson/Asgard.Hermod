using System.Globalization;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.UI.Common;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>对象页的真实布局、失败分页和本地结构导航；全程不创建 Window。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ObjectPresentationHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task FailedPagingRetainsLoadedCoordinatesAndRetriesSameTarget() => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo("en"));
        try
        {
            var server = new TestPostgreSql(); bool fail = false;
            server.PageResult = (_, _, _) => fail ? throw new InvalidOperationException("private connection details") : server.Result() with { HasMore = true };
            await using var model = new PostgreSqlTabViewModel("users", "app", PostgreSqlObjectKind.Table, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
            await model.ReadAsync(); string firstFooter = model.ObjectFooterText; var first = model.SelectedResult;
            fail = true; await model.NextCommand.ExecuteAsync(); Assert.Same(first, model.SelectedResult); Assert.Equal(firstFooter, model.ObjectFooterText); Assert.Equal(0, model.LoadedRowOffset);
            Assert.DoesNotContain("private connection details", model.Error);
            fail = false; await model.NextCommand.ExecuteAsync(); Assert.Equal(new[] { 0, 200, 200 }, server.PageReads.Select(read => read.Offset));
            string secondFooter = model.ObjectFooterText; Assert.Contains("201", secondFooter); var second = model.SelectedResult;
            fail = true; await model.PreviousCommand.ExecuteAsync(); Assert.Same(second, model.SelectedResult); Assert.Equal(secondFooter, model.ObjectFooterText);
            fail = false; await model.PreviousCommand.ExecuteAsync(); Assert.Equal(new[] { 0, 200, 200, 0, 0 }, server.PageReads.Select(read => read.Offset));
            fail = true; await model.ChangePageSizeAsync(50); Assert.Equal(firstFooter, model.ObjectFooterText); Assert.False(model.HasNextPage); Assert.False(model.HasPreviousPage);
            fail = false; await model.RefreshCommand.ExecuteAsync(); Assert.Equal(50, server.PageReads.Last().Size); Assert.Contains("50", model.ObjectFooterText); Assert.True(model.HasNextPage);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    [Fact]
    public Task StructureRefreshAndDdlNavigationPreserveLocalDraftWithoutDataRead() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
        await using var model = new PostgreSqlTabViewModel("users", "app", PostgreSqlObjectKind.Table, server, TestPostgreSql.Connection, dialogs);
        await model.ReadAsync(); var result = model.SelectedResult!; var cell = result.Rows[0].Cells[1];
        Assert.True(await model.PrepareCellAsync(result, result.Rows[0], cell)); cell.Apply("local draft");
        int pageReads = server.PageReads.Count, metadataReads = server.MetadataDatabases.Count;
        model.ObjectSection = "structure"; await model.RefreshCommand.ExecuteAsync();
        Assert.True(server.MetadataDatabases.Count > metadataReads); Assert.Equal(pageReads, server.PageReads.Count);
        Assert.Same(result, model.SelectedResult); Assert.True(model.HasDataDraft); Assert.Equal("local draft", cell.Value);
        metadataReads = server.MetadataDatabases.Count; model.ShowDefinitionCommand.Execute(null);
        Assert.Equal("definition", model.ObjectSection); Assert.Equal(metadataReads, server.MetadataDatabases.Count);
        model.ObjectSection = "data"; Assert.Same(result, model.SelectedResult); Assert.True(model.HasDataDraft); Assert.Equal(0, server.Writes); Assert.Equal(0, server.Executions);
        Assert.DoesNotContain(model.MetadataSections, choice => choice.Id == "definition");
    });

    [Theory]
    [InlineData("zh-Hans", UiTheme.Light, 1200, 1)]
    [InlineData("en", UiTheme.Dark, 520, 1)]
    [InlineData("zh-Hans", UiTheme.Dark, 1920, 2)]
    public Task ObjectGeometryAndVisibleActionsMatchReference(string language, UiTheme theme, double width, double dpi) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var server = new TestPostgreSql(); await using var model = new PostgreSqlTabViewModel("users", "app", PostgreSqlObjectKind.Table, server, TestPostgreSql.Connection, new TestPostgreSqlDialogs());
            await model.ReadAsync(); var view = new PostgreSqlTabView { DataContext = model }; var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources); root.Background = (Brush)view.FindResource("SurfaceBrush");
            DatabaseObjectScope.SetReturnToScriptCommand(root, new RelayCommand(_ => { })); using var bindings = new WpfBindingTrace();
            await Layout();
            var header = (DatabaseObjectHeader)view.FindName("ObjectContext"); Assert.Equal(58, header.ActualHeight);
            Assert.Equal(34, ((SectionSelector)view.FindName("ObjectSectionSelector")).ActualHeight);
            var toolbar = (Grid)view.FindName("ObjectToolbar"); Assert.Equal(52, toolbar.ActualHeight); Assert.Equal(92, toolbar.TranslatePoint(new(), root).Y);
            var grid = Assert.Single(WindowTestVisuals.Descendants<DataGrid>(view), item => item.Name == "ResultGrid");
            Assert.Equal(36, grid.ColumnHeaderHeight); Assert.Equal(42, grid.RowHeight); Assert.Equal(new Point(20, 144), grid.TranslatePoint(new(), root));
            var footer = (Grid)view.FindName("DataActions"); Assert.Equal(40, footer.ActualHeight); Assert.Equal(760, footer.TranslatePoint(new(), root).Y);
            Assert.Equal(model.ObjectFooterText, ((TextBlock)view.FindName("FooterStatus")).Text);
            foreach (var button in WindowTestVisuals.Descendants<Button>(root).Where(IsShown))
            {
                var origin = button.TranslatePoint(new(), root);
                Assert.InRange(origin.X, -0.1, width - button.ActualWidth + 0.1);
                Assert.InRange(origin.Y, -0.1, root.ActualHeight - button.ActualHeight + 0.1);
            }
            foreach (var button in WindowTestVisuals.Descendants<Button>(footer).Where(IsShown)) { Assert.Equal(32, button.ActualHeight); Assert.Equal(4, button.TranslatePoint(new(), footer).Y); }
            // 数据行和表头必须使用同一原生列坐标；仅检查尺寸不足以捕捉头部被行号遮挡的问题。
            var headers = WindowTestVisuals.Descendants<DataGridColumnHeader>(grid).Where(h => h.Column is not null).OrderBy(h => h.Column.DisplayIndex).ToArray();
            var cells = WindowTestVisuals.Descendants<DataGridCell>(grid).OrderBy(c => c.Column.DisplayIndex).ToArray();
            Assert.Equal(grid.Columns.Count, headers.Length);
            for (int i = 0; i < headers.Length; i++) Assert.Equal(cells[i].TranslatePoint(new(), grid).X, headers[i].TranslatePoint(new(), grid).X, 2);
            Capture("data"); model.ObjectSection = "structure"; await Layout();
            Assert.Equal(Visibility.Collapsed, footer.Visibility); Assert.Equal(40, ((Grid)view.FindName("StructureActions")).ActualHeight);
            var columnReader = (DatabaseColumnReader)view.FindName("ColumnReader"); var columnGrid = (DataGrid)columnReader.FindName("ColumnGrid");
            Assert.Equal(36, columnGrid.ColumnHeaderHeight); Assert.Equal(44, columnGrid.RowHeight); Capture("structure");
            model.ShowDefinitionCommand.Execute(null); await Layout(); Assert.Equal(Visibility.Visible, ((CodeEditor)view.FindName("DefinitionEditor")).Visibility); Capture("ddl");
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout()
            {
                for (int pass = 0; pass < 3; pass++)
                {
                    root.Measure(new(width, 800)); root.Arrange(new(0, 0, width, 800)); root.UpdateLayout();
                    VisualTreeHelper.SetRootDpi(root, new(1, 1)); VisualTreeHelper.SetRootDpi(root, new(dpi, dpi)); await WpfTestFixture.DrainDispatcherAsync();
                }
                Assert.Equal(dpi, VisualTreeHelper.GetDpi(root).DpiScaleX);
            }
            void Capture(string section)
            {
                string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return; Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap((int)(width * dpi), (int)(800 * dpi), dpi * 96, dpi * 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"postgresql-object-{section}-{language}-{theme}-{width}-{dpi}.png")); encoder.Save(output);
            }
        }
        finally { new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); Text.SetApplicationCulture(null); }
    });
    private static bool IsShown(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        return element.ActualWidth > 0 && element.ActualHeight > 0;
    }
}
