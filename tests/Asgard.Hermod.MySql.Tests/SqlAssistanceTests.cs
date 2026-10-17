using Asgard.Hermod.MySql;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

public sealed class SqlAssistanceTests
{
    private static readonly MySqlObject[] Metadata =
    [
        new(MySqlObjectKind.Database, "app", "app"), new(MySqlObjectKind.Database, "archive", "archive"),
        new(MySqlObjectKind.Table, "users", "app"), new(MySqlObjectKind.Table, "orders", "archive"),
        new(MySqlObjectKind.Column, "name", "app", "users", "VARCHAR(100)"),
        new(MySqlObjectKind.Column, "odd`column", "app", "users", "INT"),
        new(MySqlObjectKind.Column, "order_id", "archive", "orders", "BIGINT")
    ];

    [Fact]
    public void Completion_UsesAliasAfterCaretAndReplacesWholeIdentifier()
    {
        const string sql = "SELECT u.nax FROM users AS u";
        int caret = sql.IndexOf("nax", StringComparison.Ordinal) + 2;
        var item = Assert.Single(MySqlCompletion.Suggest(sql, caret, caret, 0, "app", Metadata));
        Assert.Equal("name", item.Name); Assert.Equal("`name`", item.InsertText);
        Assert.Equal("SELECT u.`name` FROM users AS u", sql.Remove(item.Start, item.Length).Insert(item.Start, item.InsertText));
    }

    [Theory]
    [InlineData("SELECT 'na", 10)]
    [InlineData("SELECT 1 -- name", 15)]
    [InlineData("SELECT /* name", 14)]
    public void Completion_DoesNotSuggestInsideLiteralsOrComments(string sql, int caret) =>
        Assert.Empty(MySqlCompletion.Suggest(sql, caret, caret, 0, "app", Metadata));

    [Fact]
    public void Completion_UsesCrossDatabaseAliasAndQuotesMetadata()
    {
        const string sql = "SELECT o. FROM archive.orders o";
        int caret = sql.IndexOf('.', StringComparison.Ordinal) + 1;
        var column = Assert.Single(MySqlCompletion.Suggest(sql, caret, caret, 0, "app", Metadata));
        Assert.Equal("`order_id`", column.InsertText);
        const string quoted = "SELECT u.od FROM `app`.`users` AS `u`";
        var escaped = Assert.Single(MySqlCompletion.Suggest(quoted, 11, 11, 0, "app", Metadata));
        Assert.Equal("`odd``column`", escaped.InsertText);
    }

    [Fact]
    public void Completion_SelectionReplacesOnlySelectionAndIncludesCurrentSchema()
    {
        const string sql = "SELECT nonsense FROM users";
        var items = MySqlCompletion.Suggest(sql, 15, 7, 8, "app", Metadata);
        var name = Assert.Single(items, item => item.Name == "name");
        Assert.Equal(7, name.Start); Assert.Equal(8, name.Length);
        Assert.Contains(items, item => item.Name == "users"); Assert.DoesNotContain(items, item => item.Name == "orders");
    }

    [Fact]
    public void Completion_QualifierFindsCrossDatabaseObjects()
    {
        const string sql = "SELECT * FROM archive.or";
        Assert.Equal("orders", Assert.Single(MySqlCompletion.Suggest(sql, sql.Length, sql.Length, 0, "app", Metadata)).Name);
    }

    [Theory]
    [InlineData("SELECT id FROM users", false, "EXPLAIN SELECT id FROM users")]
    [InlineData("SELECT id FROM users", true, "EXPLAIN ANALYZE SELECT id FROM users")]
    [InlineData("UPDATE users SET name='x'", false, "EXPLAIN UPDATE users SET name='x'")]
    [InlineData("-- comment\nSELECT 1", true, "EXPLAIN ANALYZE -- comment\nSELECT 1")]
    public void Plan_PreservesStatementAndOnlyAddsRequestedExplain(string sql, bool analyze, string expected) =>
        Assert.Equal(expected, MySqlExecutionPlan.Create(MySqlSql.Split(sql), analyze));

    [Theory]
    [InlineData("SELECT 1; DELETE FROM users", false)]
    [InlineData("DELETE FROM users", true)]
    [InlineData("SELECT * FROM users FOR UPDATE", true)]
    [InlineData("SELECT 1 INTO OUTFILE '/tmp/file'", true)]
    [InlineData("SELECT @x:=1", true)]
    [InlineData("SELECT 1 /*!80000 INTO OUTFILE '/tmp/file' */", true)]
    [InlineData("DELIMITER $$\nSELECT 1; DELETE FROM users$$", false)]
    [InlineData("CALL p()", false)]
    public void Plan_RejectsUnsupportedOrPotentiallyWritingAnalysis(string sql, bool analyze) =>
        Assert.Throws<InvalidOperationException>(() => MySqlExecutionPlan.Create(MySqlSql.Split(sql), analyze));
}
