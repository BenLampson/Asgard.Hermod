using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;
using Npgsql;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>协议只保留位置数字，服务器原文不进入展示；Unicode 字符位置与 WPF 偏移分别计算。</summary>
public sealed class ErrorLocationTests
{
    [Fact]
    public void ServerCharacterPositionMapsAcrossTrimCrLfAndSurrogatePair()
    {
        const string sql = "SELECT 1;\r\n  SELECT '😀', missing;";
        var statement = PostgreSqlSql.Split(sql)[1];
        var raw = new PostgresException("password=private", "ERROR", "ERROR", "42703", position: 13);
        var failure = PostgreSqlFailure.Safe(raw).AtStatement(sql, statement.Start, statement.Length, 2);
        var location = Assert.IsType<SqlErrorLocation>(failure.Location);
        Assert.True(location.IsExact); Assert.Equal(sql.IndexOf("missing", StringComparison.Ordinal), location.Offset);
        Assert.Equal(2, location.Line); Assert.Equal(15, location.Column); Assert.Equal(2, location.Statement);
        Assert.DoesNotContain("private", Text.Error(failure, "PostgreSqlCommandFailed")); Assert.DoesNotContain("private", failure.ToString());
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000000)]
    public void MissingOrInvalidPositionFallsBackToStatementStart(int position)
    {
        const string sql = "\n  SELECT broken";
        var failure = PostgreSqlFailure.Safe(new PostgresException("secret raw error", "ERROR", "ERROR", "42601", position: position)).AtStatement(sql, 0, sql.Length, 1);
        Assert.Equal(new SqlErrorLocation(1, 3, 2, 3, false), failure.Location);
    }
    [Fact]
    public void InvalidStatementRangeHasNoInventedLocation()
    {
        Assert.Null(SqlErrorLocation.InStatement("SELECT 1", -1, 2, 1)); Assert.Null(SqlErrorLocation.InStatement("SELECT 1", 0, int.MaxValue, 1));
        Assert.Null(SqlErrorLocation.InStatement("   ", 0, 3, 1)); Assert.Null(SqlErrorLocation.AtOffset("x", 1, 2, true));
    }
}
