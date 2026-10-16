using System.Globalization;
using System.Windows.Media.Imaging;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>定位仅消费对应发送文稿，不改写草稿或执行 SQL；纯控件离屏渲染。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class SqlMessagesHeadlessTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en", UiTheme.Dark)]
    [InlineData("zh-Hans", UiTheme.Light)]
    public Task FailedCurrentStatementMapsTrimmedRangeAndEditingInvalidatesLocation(string language, UiTheme theme) => fixture.RunOffscreenAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language)); new ThemeService(Application.Current.Resources).SetTheme(theme);
        try
        {
            var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs();
            server.ExecutionResult = sql => new([], [new(1, 0, false)], new PostgreSqlFailure("PostgreSqlStatementRejected", "POSTGRESQL_STATEMENT", "42601").AtStatement(sql, 0, sql.Length, 1), TimeSpan.FromMilliseconds(3), new(true, false));
            await using var model = new PostgreSqlTabViewModel("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
            model.Sql = "SELECT 1;\r\n  SELECT missing_column FROM users;"; string document = model.Sql;
            using var bindings = new WpfBindingTrace(); var view = new PostgreSqlTabView { DataContext = model }; var root = new Border { Child = view };
            root.Resources.MergedDictionaries.Add(Application.Current.Resources); root.Background = (Brush)view.FindResource("SurfaceBrush"); await Layout();
            await model.ExecuteScopeAsync("", document.Length, false); await Layout();
            Assert.Equal(document, model.Sql); Assert.Equal("messages", model.OutputView); Assert.True(model.IsOutputFooterVisible);
            var row = Assert.Single(model.OutputMessages); int expected = document.IndexOf("SELECT missing", StringComparison.Ordinal);
            Assert.Equal(expected, row.GetOffset(document)); Assert.Equal(2, row.Line); Assert.True(model.LocateMessageCommand.CanExecute(row));
            model.LocateMessageCommand.Execute(row); var editor = ((DatabaseSqlWorkspace)view.FindName("SqlWorkspace")).Editor;
            Assert.Equal(expected, editor.SelectionStart); Assert.Equal(document, model.Sql); Assert.Single(server.ExecutedSql); Assert.Equal(0, server.Writes);
            var grid = (DataGrid)((DatabaseExecutionMessages)((DatabaseSqlWorkspace)view.FindName("SqlWorkspace")).FindName("MessageReader")).FindName("MessageGrid");
            Assert.Equal(36, grid.ColumnHeaderHeight); Assert.Equal(40, grid.RowHeight); Assert.Equal(4, grid.Columns.Count); Assert.Equal(120, grid.Columns[0].ActualWidth);
            await model.ExportOutputAsync(); Assert.Equal(4, dialogs.ExportedGrid!.Columns.Count); Assert.Single(dialogs.ExportedGrid.Rows); Assert.Contains(row.Message, model.MessageCopyText); Assert.Null(model.SelectedResult); Assert.Single(server.ExecutedSql);
            Capture(); model.Sql += "\r\n-- edit"; Assert.Null(row.GetOffset(model.Sql)); Assert.False(model.LocateMessageCommand.CanExecute(row));
            int selected = editor.SelectionStart; model.LocateMessageCommand.Execute(row); Assert.Equal(selected, editor.SelectionStart); Assert.Single(server.ExecutedSql);
            Assert.Empty(bindings.Messages); Assert.Null(Window.GetWindow(root)); Assert.Null(PresentationSource.FromVisual(root));
            async Task Layout() { for (int i = 0; i < 3; i++) { root.Measure(new(1362, 856)); root.Arrange(new(0, 0, 1362, 856)); root.UpdateLayout(); await WpfTestFixture.DrainDispatcherAsync(); } }
            void Capture()
            {
                string? directory = Environment.GetEnvironmentVariable("HERMOD_OFFSCREEN_OUTPUT"); if (directory is null) return; Directory.CreateDirectory(directory);
                var bitmap = new RenderTargetBitmap(1362, 856, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(directory, $"postgresql-failed-sql-{language}-{theme}.png")); encoder.Save(output);
            }
        }
        finally { Text.SetApplicationCulture(null); new ThemeService(Application.Current.Resources).SetTheme(UiTheme.Light); }
    });
    [Fact]
    public Task SelectionOffsetUsesExactSelectedRangeAndUnknownLocationStaysDisabled() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestPostgreSql(); var dialogs = new TestPostgreSqlDialogs(); server.ExecutionResult = sql => new([], [new(1, 0, false)], new PostgreSqlFailure("PostgreSqlCommandFailed", "POSTGRESQL_COMMAND").AtStatement(sql, 0, sql.Length, 1), TimeSpan.Zero, new(true, false));
        await using var model = new PostgreSqlTabViewModel("Query", "app", PostgreSqlObjectKind.Root, server, TestPostgreSql.Connection, dialogs);
        model.Sql = "SELECT 1;\nSELECT 1;"; const string selection = "SELECT 1;";
        await model.ExecuteScopeAsync(selection, model.Sql.Length, false, 10); Assert.Equal(10, Assert.Single(model.OutputMessages).GetOffset(model.Sql));
        await model.ExecuteScopeAsync(selection, model.Sql.Length, false); Assert.Null(Assert.Single(model.OutputMessages).GetOffset(model.Sql));
        Assert.False(model.LocateMessageCommand.CanExecute(model.OutputMessages[0])); Assert.Equal(0, server.Writes);
    });
}
