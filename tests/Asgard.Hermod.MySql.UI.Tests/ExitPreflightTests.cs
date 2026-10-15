using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>验证实际模块关闭预检延后丢弃、回滚和停止查询，不以无副作用页面替代。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class ExitPreflightTests(WpfTestFixture fixture)
{
    /// <summary>其他窗口后来取消退出时，先前选择丢弃的真实 MySQL 草稿和事务仍在。</summary>
    [Fact]
    public Task LaterCancellation_PreservesEarlierDraftAndTransaction() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { InTransaction = true };
        var configurations = new Configurations();
        await using var first = new MySqlWorkspaceViewModel(new(new("one", "Database", "Database", "database", [], TypeId: MySqlIds.Type)),
            configurations, server, configurations, new TestMySqlDialogs { Choice = MySqlDraftChoice.Discard }, "app");
        await first.InitializeAsync(TestContext.Current.CancellationToken);
        var tab = first.ActiveTab!; await tab.ExecuteAsync("SELECT * FROM users");
        var grid = tab.SelectedResult!; var row = grid.Rows[0];
        Assert.True(await tab.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("keep this draft");
        var plan = await first.PrepareCloseAsync(); Assert.NotNull(plan);
        await using var second = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, new TestMySql(), TestMySql.Connection, new TestMySqlDialogs());
        second.Sql = "unsaved SQL";
        Assert.Null(await second.PrepareCloseAsync());
        Assert.True(tab.HasDataDraft); Assert.Equal("keep this draft", row.Cells[1].Value);
        Assert.True(server.InTransaction); Assert.Equal(0, server.Rollbacks); Assert.Equal(0, server.Disposals);
        Assert.Equal("unsaved SQL", second.Sql);
        await plan.CommitAsync();
        Assert.False(tab.HasDataDraft); Assert.Equal(1, server.Rollbacks);
        await plan.CommitAsync(); Assert.Equal(1, server.Rollbacks);
    });

    /// <summary>预检得到停止许可后仍保留执行中的查询，只有统一提交才取消。</summary>
    [Fact]
    public Task Prepare_DoesNotStopRunningQueryUntilCommit() => fixture.RunAsync(async () =>
    {
        var server = new TestMySql { BlockedQuery = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var tab = new MySqlTabViewModel("Query", "app", MySqlObjectKind.Root, server, TestMySql.Connection, new TestMySqlDialogs());
        Task query = tab.ExecuteAsync("SELECT SLEEP(10)"); await Task.Yield(); Assert.True(tab.IsBusy);
        var plan = await tab.PrepareCloseAsync(); Assert.NotNull(plan);
        Assert.True(tab.IsBusy); Assert.False(query.IsCompleted); Assert.Equal(0, server.Disposals);
        await plan.CommitAsync(); await query;
        Assert.False(tab.IsBusy);
    });

    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
