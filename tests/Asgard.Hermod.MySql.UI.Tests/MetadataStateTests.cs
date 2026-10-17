using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>名称和环境更新不重建协议会话、SQL 文稿或结果草稿。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class MetadataStateTests(WpfTestFixture fixture)
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    public Task Workspace_MetadataRefreshPreservesQueryAndRowDrafts(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var instance = new InstanceItemViewModel(new("mysql", "Database", "Database", "database", [], TypeId: MySqlIds.Type)
            { TagIds = ["builtin.environment.production"] });
            var server = new TestMySql(); var configurations = new Configurations(); var dialogs = new TestMySqlDialogs();
            await using var workspace = new MySqlWorkspaceViewModel(instance, configurations, server, configurations, dialogs, "app");
            await workspace.InitializeAsync(TestContext.Current.CancellationToken);
            var query = workspace.ActiveTab!; query.Sql = "SELECT * FROM users"; await query.ExecuteAsync(query.Sql);
            var grid = query.SelectedResult!; var row = grid.Rows[0];
            Assert.True(await query.PrepareCellAsync(grid, row, row.Cells[1])); row.Cells[1].Apply("unsaved row");
            query.Sql += " WHERE id = 1";
            await workspace.OpenObjectAsync(new(MySqlObjectKind.Table, "users", "app")); var table = workspace.ActiveTab!;
            int opens = server.Opens, executions = server.Executions, disposals = server.Disposals; string pageId = workspace.Id;
            dialogs.FieldOpened = field =>
            {
                field.Input = "{\"unsaved\":true}"; var notified = new List<string?>(); field.PropertyChanged += (_, args) => notified.Add(args.PropertyName);
                instance.UpdateSnapshot(instance.Summary with { Name = "Renamed database", TagIds = ["builtin.environment.development"] });
                workspace.UpdateInstanceMetadata(instance.Summary);
                Assert.Equal(Text.Format("MySqlFieldOperationTarget", instance.Name, Text.Get("Development"),
                    Text.Format("MySqlFieldContext", "app", "users", "profile", "id=1")), field.Context);
                Assert.Contains(nameof(field.Context), notified); Assert.Equal("{\"unsaved\":true}", field.Input);
                return Task.CompletedTask;
            };
            await table.OpenFieldAsync(table.SelectedResult!, table.SelectedResult!.Rows[0], table.SelectedResult.Rows[0].Cells[2]);
            Assert.Equal(pageId, workspace.Id); Assert.Equal(Text.Format("DatabaseSessionWorkspaceTitle", instance.Name), workspace.Header);
            Assert.Same(query, workspace.Tabs[0]); Assert.Same(table, workspace.ActiveTab); Assert.Same(grid, query.SelectedResult);
            Assert.Equal("SELECT * FROM users WHERE id = 1", query.Sql); Assert.True(query.IsDocumentDirty); Assert.True(query.HasDataDraft);
            Assert.Equal("unsaved row", row.Cells[1].Value); Assert.Equal("app", query.Database); Assert.Equal("app", table.Database);
            Assert.Equal(opens, server.Opens); Assert.Equal(executions, server.Executions); Assert.Equal(disposals, server.Disposals); Assert.Equal(0, server.Writes);
        }
        finally { Text.SetApplicationCulture(null); }
    });

    private sealed class Configurations : IInstanceConfigurationService, IInstanceEditor
    {
        public IReadOnlyList<InstanceSummary> GetInstances() => [];
        public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(TestMySql.Connection);
        public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    }
}
