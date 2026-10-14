using Xunit;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>不连接数据库，验证真实表读取计划的边界、标识符与参数化条件。</summary>
public sealed class MySqlTableReadPlanTests
{
    private static MySqlTable Table => new("app", "users", [
        new(0, "id", "INT", typeof(int), false, "app", "users", "id", true),
        new(1, "name", "TEXT", typeof(string), true, "app", "users", "name"),
        new(2, "active", "BOOL", typeof(bool), true, "app", "users", "active")], "InnoDB");

    [Theory]
    [InlineData(50)]
    [InlineData(100)]
    [InlineData(200)]
    [InlineData(500)]
    public void PageSize_UsesBoundedLookAheadAndActualOffset(int pageSize)
    {
        var plan = MySqlTableReadPlan.Create(Table, 1000, pageSize, "");
        Assert.Equal("SELECT * FROM `app`.`users` ORDER BY `id` LIMIT @limit OFFSET @offset", plan.Sql);
        Assert.Equal(pageSize + 1, plan.Parameters["@limit"]); Assert.Equal(1000, plan.Parameters["@offset"]);
    }

    [Fact]
    public void Filter_QuotesCatalogColumnsAndKeepsLiteralSqlCharactersOnlyInParameters()
    {
        var plan = MySqlTableReadPlan.Create(Table, 0, 50, "WHERE `id` >= 10 AND (name LIKE 'a''; DROP TABLE users; -- /* literal */' OR active IS NULL)");
        Assert.Contains("`id` >= @filter0", plan.Sql); Assert.Contains("`name` LIKE @filter1", plan.Sql); Assert.Contains("`active` IS NULL", plan.Sql);
        Assert.Equal(10m, plan.Parameters["@filter0"]);
        Assert.Equal("a'; DROP TABLE users; -- /* literal */", plan.Parameters["@filter1"]);
        Assert.DoesNotContain("DROP", plan.Sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("--", plan.Sql);
    }

    [Theory]
    [InlineData("unknown = 1")]
    [InlineData("id = 1; DELETE FROM users")]
    [InlineData("id = 1 UNION SELECT 1")]
    [InlineData("id = SLEEP(1)")]
    [InlineData("id = 1 /* comment */")]
    [InlineData("1 = 1")]
    [InlineData("name = 'unfinished")]
    [InlineData("name = @raw")]
    [InlineData("id IN ()")]
    public void UnsafeOrUnsupportedGrammar_IsRejected(string filter) => Assert.ThrowsAny<Exception>(() => MySqlTableReadPlan.Create(Table, 0, 200, filter));

    [Fact]
    public void ComparisonsAndLists_PreserveTypedValuesAndRejectExcessComplexity()
    {
        var plan = MySqlTableReadPlan.Create(Table, 0, 100, "NOT (id IN (1, -2, 3.5) OR active = TRUE) AND name NOT LIKE 'B%'");
        Assert.Contains("NOT IN", MySqlTableReadPlan.Create(Table, 0, 100, "id NOT IN (1)").Sql);
        Assert.Equal(5, plan.Parameters.Count - 2); Assert.Contains(true, plan.Parameters.Values);
        Assert.ThrowsAny<Exception>(() => MySqlTableReadPlan.Create(Table, 0, 100, "id IN (" + string.Join(",", Enumerable.Range(0, 65)) + ")"));
        Assert.ThrowsAny<Exception>(() => MySqlTableReadPlan.Create(Table, 0, 100, new string('(', 20) + "id=1" + new string(')', 20)));
    }
}
