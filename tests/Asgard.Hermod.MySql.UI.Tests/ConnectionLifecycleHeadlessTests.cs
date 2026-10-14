using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>假 MySQL 会话覆盖首次失败与显式重连，不创建 HWND 或连接数据库。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ConnectionLifecycleHeadlessTests(WpfTestFixture fixture)
{
    [Fact]
    public Task InitialFailure_DisablesCommandsAndReportsSharedStartupContract() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql { OpenError = new IOException("password=private") }; var configs = new Configurations();
        await using var page = new MySqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, new TestMySqlDialogs());
        await page.InitializeAsync();
        Assert.NotNull(page.InitialConnectionFailure); Assert.False(page.HasConnectedOnce); Assert.False(page.IsConnected);
        Assert.False(page.NewQueryCommand.CanExecute(null)); Assert.False(page.OpenQueryCommand.CanExecute(null));
        Assert.Null(page.NewQuery()); Assert.Empty(page.Tabs); Assert.True(page.Connection.IsOpen);
        Assert.DoesNotContain("private", page.InitialConnectionFailure!);
    });

    [Fact]
    public Task Reconnect_PreservesSqlAndCellDraftsAndReleasesOldSessions() => fixture.RunOffscreenAsync(async () =>
    {
        var server = new TestMySql(); var configs = new Configurations(); var dialogs = new TestMySqlDialogs();
        await using var page = new MySqlWorkspaceViewModel(new(Configurations.Instance), configs, server, configs, dialogs);
        await page.InitializeAsync(); var query = page.ActiveTab!; query.Sql = "SELECT 1;";
        await page.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = page.ActiveTab!;
        var grid = table.SelectedResult!; var row = grid.Rows[0]; await table.PrepareCellAsync(grid, row, row.Cells[1]); row.Cells[1].Apply("kept draft");
        var previous = server.Sessions.ToArray(); previous[0].Disconnect();
        Assert.False(page.IsConnected); Assert.False(query.CanRun); Assert.True(page.Connection.IsOpen);
        page.Connection.DismissCommand.Execute(null); Assert.False(page.NewQueryCommand.CanExecute(null));
        await page.ReconnectCommand.ExecuteAsync();
        Assert.True(page.IsConnected); Assert.True(page.HasConnectedOnce); Assert.Null(page.InitialConnectionFailure);
        Assert.Same(query, page.Tabs[0]); Assert.Same(table, page.ActiveTab); Assert.Same(grid, table.SelectedResult);
        Assert.Equal("SELECT 1;", query.Sql); Assert.Equal("kept draft", row.Cells[1].Value); Assert.True(table.HasDataDraft);
        Assert.True(query.CanRun); Assert.All(previous, session => Assert.False(session.IsConnected));
        Assert.Equal(0, server.Writes); Assert.Equal(0, server.Executions);
        previous[0].Disconnect(); Assert.False(page.Connection.HasError);
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
