using Asgard.Hermod.PostgreSql;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>执行边界保留 PostgreSQL 例程、字符串、标识符和注释中的分号。</summary>
public sealed class PostgreSqlSqlTests
{
    /// <summary>与服务器词法一致地拆分两个完整语句。</summary>
    [Theory]
    [InlineData("SELECT ';'; SELECT 2;", "SELECT ';'")]
    [InlineData("SELECT 'it''s;ok'; SELECT 2;", "SELECT 'it''s;ok'")]
    [InlineData("SELECT E'it\\'s;ok'; SELECT 2;", "SELECT E'it\\'s;ok'")]
    [InlineData("SELECT \"a;\"\"b\"; SELECT 2;", "SELECT \"a;\"\"b\"")]
    [InlineData("DO $$BEGIN PERFORM 1; PERFORM 2; END$$; SELECT 2;", "DO $$BEGIN PERFORM 1; PERFORM 2; END$$")]
    [InlineData("SELECT $标记$one;two$标记$; SELECT 2;", "SELECT $标记$one;two$标记$")]
    [InlineData("/* outer /* inner; */ still; */ SELECT 1; SELECT 2;", "/* outer /* inner; */ still; */ SELECT 1")]
    [InlineData("--no whitespace;\nSELECT 1; SELECT 2;", "--no whitespace;\nSELECT 1")]
    public void Split_PreservesPostgreSqlLexicalBoundaries(string sql, string first)
    {
        var statements = PostgreSqlSql.Split(sql);
        Assert.Equal(2, statements.Count);
        Assert.Equal(first, statements[0].Sql);
        Assert.Equal("SELECT 2", statements[1].Sql);
        Assert.All(statements, item => Assert.Equal(item.Sql, sql.Substring(item.Start, item.Length).Trim()));
    }

    /// <summary>SQL 标准例程正文和 CASE 内部表达式一起提交。</summary>
    [Fact]
    public void Split_PreservesBeginAtomicBody()
    {
        const string function = "CREATE FUNCTION f() RETURNS int LANGUAGE SQL BEGIN ATOMIC SELECT CASE WHEN true THEN 1 ELSE 2 END; SELECT 3; END";
        var statements = PostgreSqlSql.Split(function + "; SELECT f();");
        Assert.Equal(2, statements.Count);
        Assert.Equal(function, statements[0].Sql);
    }

    /// <summary>当前会话关闭标准字符串时，普通字符串同样解释反斜杠转义。</summary>
    [Fact]
    public void Split_UsesActualSessionStringMode()
    {
        const string sql = "SELECT 'it\\'s;ok'; SELECT 2;";
        Assert.Equal(2, PostgreSqlSql.Split(sql, standardConformingStrings: false).Count);
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSql.Split(sql));
        Assert.Equal("EXPLAIN (FORMAT JSON) SELECT 'it\\'s;ok'", PostgreSqlSql.Explain("SELECT 'it\\'s;ok'", false));
    }

    /// <summary>注释及空分隔符不会被当成需要执行的命令。</summary>
    [Fact]
    public void Split_IgnoresCommentOnlyInputAndDollarIdentifierParts()
    {
        Assert.Empty(PostgreSqlSql.Split("; --comment\n /* nested /* ; */ */ ;"));
        Assert.Equal(2, PostgreSqlSql.Split("SELECT name$tag$ FROM example; SELECT 2").Count);
        Assert.Equal(2, PostgreSqlSql.Split("SELECT $1; SELECT $2").Count);
    }

    /// <summary>不完整的文稿必须先修复，不能发送已经拆出的前半部分。</summary>
    [Theory]
    [InlineData("SELECT 1; SELECT 'unfinished")]
    [InlineData("SELECT $tag$unfinished$$")]
    [InlineData("SELECT \"unfinished")]
    [InlineData("SELECT 1; /* outer /* inner */")]
    [InlineData("CREATE FUNCTION f() RETURNS int LANGUAGE SQL BEGIN ATOMIC SELECT 1;")]
    public void Split_RejectsIncompleteDocuments(string sql) =>
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSql.Split(sql));

    /// <summary>计划按钮只接受一个完整命令，始终使用不执行语句的估算模式。</summary>
    [Fact]
    public void Explain_RejectsExtraStatementsAndNeverAddsAnalyze()
    {
        Assert.Equal("EXPLAIN (FORMAT JSON) SELECT 1", PostgreSqlSql.Explain("SELECT 1;"));
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSql.Explain("SELECT 1; DELETE FROM example"));
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSql.Explain("--empty"));
    }

    /// <summary>对象名保持原样并转义内部双引号，不使用 MySQL 的反引号。</summary>
    [Fact]
    public void Quote_PreservesIdentifierCaseAndQuotes() => Assert.Equal("\"Odd\"\"Name\"", PostgreSqlSql.Quote("Odd\"Name"));
}
