using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>真实模块和连接工厂驱动假协议请求，覆盖分页、筛选、草稿及文稿上下文。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class TableInteractionHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task PagingAndWhere_ReachSessionAndRespectDraftCancellation() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var dialogs = new TestMySqlDialogs(); var configs = new Configurations();
        server.PageResult = (offset, _, _) => server.Result() with { HasMore = offset == 0 };
        await using var page = new MySqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, dialogs);
        await page.InitializeAsync(); await page.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = page.ActiveTab!;
        Assert.True(table.HasNextPage); Assert.False(table.HasPreviousPage);
        await table.ChangePageSizeAsync(50); Assert.Equal(50, server.PageReads.Last().Size);
        await table.NextCommand.ExecuteAsync(); Assert.Equal(50, table.Offset); Assert.False(table.HasNextPage); Assert.True(table.HasPreviousPage);
        var grid = table.SelectedResult!; var row = grid.Rows[0]; await table.PrepareCellAsync(grid, row, row.Cells[1]); row.Cells[1].Apply("kept");
        int reads = server.PageReads.Count; table.FilterText = "id >= 1";
        await table.ApplyFilterCommand.ExecuteAsync(); await table.ChangePageSizeAsync(100);
        Assert.Equal(reads, server.PageReads.Count); Assert.Equal(50, table.Offset); Assert.Equal(50, table.PageSize); Assert.True(table.HasDataDraft);
        dialogs.Choice = MySqlDraftChoice.Discard;
        await table.ApplyFilterCommand.ExecuteAsync(); Assert.Equal(0, table.Offset); Assert.Equal("id >= 1", server.PageReads.Last().Filter);
        Assert.Equal("id >= 1", table.AppliedFilter); Assert.False(table.HasDataDraft);
        await table.ChangePageSizeAsync(100); Assert.Equal(100, server.PageReads.Last().Size); Assert.Equal("id >= 1", server.PageReads.Last().Filter);
        await table.ClearFilterCommand.ExecuteAsync(); Assert.Equal("", server.PageReads.Last().Filter);
        table.FilterText = "id=1; DELETE FROM users"; reads = server.PageReads.Count; await table.ApplyFilterCommand.ExecuteAsync();
        Assert.Equal(reads, server.PageReads.Count); Assert.True(table.HasError); Assert.Equal("", table.AppliedFilter);
        server.PageResult = (_, _, _) => server.Result(); await table.RefreshCommand.ExecuteAsync(); Assert.False(table.ShowPageNavigation);
        Assert.Equal(0, server.Writes);
    });

    [Fact]
    public Task NewQuery_UsesSelectedSchemaThenActiveContextAndKeepsOlderDocuments() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var configs = new Configurations();
        await using var page = new MySqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, new TestMySqlDialogs());
        await page.InitializeAsync(); var original = page.ActiveTab!; await original.ChangeDatabaseAsync("active-context");
        var schema = Assert.Single(page.Tree!.Root.Children); page.SelectedNode = schema;
        var selected = page.NewQuery()!; Assert.Equal("app", selected.Database); Assert.Equal("active-context", original.Database);
        page.SelectedNode = null; await selected.ChangeDatabaseAsync("manual-context");
        var next = page.NewQuery()!; Assert.Equal("manual-context", next.Database);
        Assert.False(next.ShowTableControls); await next.ChangePageSizeAsync(50); Assert.Equal(200, next.PageSize);
        Assert.Empty(server.PageReads);
    });

    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        internal static InstanceSummary Instance => new("mysql", "MySQL", "MySQL", "database", [], TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct);
        public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
