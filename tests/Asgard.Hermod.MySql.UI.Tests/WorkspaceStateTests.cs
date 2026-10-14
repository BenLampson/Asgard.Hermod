using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class WorkspaceStateTests(WpfTestFixture fixture)
{
    [Fact]
    public Task Grid_StagesInlineAndWindowChangesUntilExplicitSave() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); await using var tab = Tab(server, dialogs);
        await tab.ReadAsync(); var grid = Assert.IsType<MySqlGridViewModel>(tab.SelectedResult); var row = grid.Rows[0]; var cell = row.Cells[1];
        Assert.True(await tab.PrepareCellAsync(grid, row, cell)); cell.Apply("inline");
        var field = new MySqlFieldEditorViewModel(row.Cells[2], "app.users.profile", true); field.Input = "{\"updated\":true}"; Assert.True(field.Apply());
        Assert.Equal(0, server.Writes); Assert.Equal(2, grid.DirtyCount); Assert.Equal("Alice", server.Values["name"]);
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.Equal(1, server.Writes); Assert.False(tab.HasDataDraft); Assert.Equal("inline", server.Values["name"]);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task UnknownWrite_IsNeverReplayedAndVerificationResolvesTypedBaseline(bool committed) => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { Unknown = true, CommitBeforeUnknown = committed }; await using var tab = Tab(server, new());
        await tab.ReadAsync(); var grid = tab.SelectedResult!; var row = grid.Rows[0]; Assert.True(await tab.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("draft");
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.True(tab.OutcomeUnknown); Assert.True(tab.HasDataDraft);
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.Equal(1, server.Writes);
        await tab.VerifyCommand.ExecuteAsync(); Assert.False(tab.OutcomeUnknown); Assert.Equal(!committed, tab.HasDataDraft); Assert.Equal(1, server.Writes);
    });

    [Fact]
    public Task Conflict_RetainsDraftAndCancellationKeepsResults() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); await using var tab = Tab(server, dialogs);
        await tab.ReadAsync(); var grid = tab.SelectedResult!; var row = grid.Rows[0]; await tab.PrepareCellAsync(grid, row, row.Cells[1]); row.Cells[1].Apply("draft"); server.Values["name"] = "server";
        await tab.SaveChangesCommand.ExecuteAsync(); Assert.Equal(1, dialogs.Conflicts); Assert.Equal("draft", row.Cells[1].Value); Assert.True(tab.HasDataDraft);
        Assert.False(await tab.RequestCloseAsync()); Assert.Same(grid, tab.SelectedResult);
    });

    [Fact]
    public Task Query_DraftConfirmationPreventsResultReplacementAndSavesSqlSeparately() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); await using var tab = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, dialogs);
        tab.Sql = "SELECT * FROM users"; await tab.ExecuteAsync(tab.Sql); var grid = tab.SelectedResult!; var row = grid.Rows[0]; await tab.PrepareCellAsync(grid, row, row.Cells[1]); row.Cells[1].Apply("draft");
        await tab.ExecuteAsync("SELECT 2"); Assert.Same(grid, tab.SelectedResult); Assert.Equal(1, server.Executions);
        await tab.SaveDocumentAsync(); Assert.False(tab.IsDocumentDirty); Assert.True(tab.HasDataDraft); Assert.Equal(0, server.Writes);
    });

    [Fact]
    public Task FieldEditor_ValidatesJsonAndPreservesBinaryNullAndEmpty() => fixture.RunAsync(() =>
    {
        var row = new MySqlGridViewModel(new TestMySql().Result()).Rows[0];
        var json = new MySqlFieldEditorViewModel(row.Cells[2], "profile", true); json.Input = "{"; Assert.False(json.CanApply); Assert.False(json.Apply()); Assert.Equal("{", json.Input);
        json.IsNull = true; Assert.True(json.Apply()); Assert.Null(row.Cells[2].Value);
        var binary = new MySqlFieldEditorViewModel(row.Cells[3], "payload", true); binary.Format = "base64"; Assert.Equal("AP8=", binary.Input); binary.Input = "AP+A"; Assert.True(binary.Apply()); Assert.Equal(new byte[] { 0, 255, 128 }, row.Cells[3].Value);
        var text = new MySqlFieldEditorViewModel(row.Cells[1], "name", true); text.Input = ""; Assert.True(text.Apply()); Assert.Equal("", row.Cells[1].Value);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Tree_UsesTypesAndFiltersOnlyLoadedHierarchyAndKeepsErrorsDistinct() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql(); await using var session = await server.OpenAsync(TestMySql.Connection, TestContext.Current.CancellationToken); using var tree = new MySqlObjectTree(session);
        await tree.LoadAsync(tree.Root); var database = tree.Root.Children[0]; await tree.LoadAsync(database); var tables = database.Children[0]; await tree.LoadAsync(tables);
        Assert.Equal(new[] { "table-2", "eye", "workflow", "square-function", "clock-3" }, database.Children.Select(node => node.Icon));
        tree.Filter("users"); Assert.True(database.IsVisible); Assert.True(tables.IsVisible); Assert.False(database.Children[1].IsVisible); tree.Filter(""); Assert.True(database.Children[1].IsVisible);
        server.ListFailure = new IOException("credential=secret"); await tree.RefreshAsync(tables); Assert.NotEmpty(tables.Status); Assert.True(tables.HasMore); Assert.Contains(tables.Children, node => node.Object.Name == "users"); Assert.DoesNotContain("secret", tables.Status);
    });

    private static MySqlTabViewModel Tab(TestMySql server, TestMySqlDialogs dialogs) => new("users", "app", MySqlObjectKind.Table, server, TestMySql.Connection, dialogs);
    [Fact]
    public Task Query_CancellationAllowsRecoveryWithoutReentry() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var tab = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        var pending = tab.ExecuteAsync("SELECT SLEEP(10)"); await Task.Yield(); Assert.True(tab.IsBusy);
        await tab.ExecuteAsync("SELECT 2"); Assert.Equal(1, server.Executions);
        tab.CancelCommand.Execute(null); await pending; Assert.False(tab.IsBusy);
        server.BlockedQuery = null; await tab.ExecuteAsync("SELECT 3"); Assert.Equal(2, server.Executions);
    });
}
