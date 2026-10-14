using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.MySql;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>对象定位与结构切换保持数据库、会话和未保存数据独立。</summary>
[Collection(WpfTestCollection.Name)]
public sealed class StructureNavigationTests(WpfTestFixture fixture)
{
    /// <summary>分区只筛选已读元数据，切换 DDL 不重读数据或丢失网格草稿。</summary>
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public Task StructureSections_PreserveObjectContextAndDataDraft(string language) => fixture.RunAsync(async () =>
    {
        Text.SetApplicationCulture(CultureInfo.GetCultureInfo(language));
        try
        {
            var server = new TestMySql();
            await using var tab = new MySqlTabViewModel("users", "app", MySqlObjectKind.Table, server, TestMySql.Connection, new TestMySqlDialogs());
            await tab.ReadAsync(); var grid = tab.SelectedResult!;
            Assert.True(await tab.PrepareCellAsync(grid, grid.Rows[0], grid.Rows[0].Cells[1])); grid.Rows[0].Cells[1].Apply("unsaved");
            int opens = server.Opens;
            Assert.Equal("app", tab.DatabaseContext); Assert.All(tab.StructureRows, item => Assert.Equal(MySqlObjectKind.Column, item.Kind));
            Assert.Equal(4, tab.StructureRows.Count); Assert.False(tab.IsEmptyStructure);
            tab.Section = "structure"; tab.StructureSection = "indexes";
            Assert.Empty(tab.StructureRows); Assert.True(tab.IsEmptyStructure);
            tab.StructureSection = "definition"; Assert.True(tab.IsDefinitionSelected); Assert.False(tab.IsEmptyStructure);
            Assert.Contains("CREATE TABLE users", tab.Definition);
            tab.StructureSection = "columns"; tab.Section = "data";
            Assert.Same(grid, tab.SelectedResult); Assert.True(tab.HasDataDraft); Assert.Equal("unsaved", grid.Rows[0].Cells[1].Value);
            Assert.Equal(opens, server.Opens); Assert.Equal(0, server.Writes); Assert.Equal("app", tab.DatabaseContext);
            Assert.Equal(language == "zh-Hans" ? "数据库 / Schema" : "Database / schema", Text.Get("MySqlDatabaseSchema"));
        }
        finally { Text.SetApplicationCulture(null); }
    });
    /// <summary>视图和存储程序仅显示适用分区，不伪造对象目录。</summary>
    [Theory]
    [InlineData(MySqlObjectKind.View, "columns", 2)]
    [InlineData(MySqlObjectKind.Procedure, "parameters", 2)]
    [InlineData(MySqlObjectKind.Function, "parameters", 2)]
    [InlineData(MySqlObjectKind.Event, "definition", 1)]
    [InlineData(MySqlObjectKind.Trigger, "definition", 1)]
    public async Task ObjectKind_LimitsAvailableStructureSections(MySqlObjectKind kind, string first, int count)
    {
        await using var tab = new MySqlTabViewModel("object", "schema", kind, new TestMySql(), TestMySql.Connection, new TestMySqlDialogs());
        Assert.Equal(first, tab.StructureSection); Assert.Equal(count, tab.StructureSections.Count);
        tab.StructureSection = "unknown-section"; Assert.Equal(first, tab.StructureSection);
    }
}
