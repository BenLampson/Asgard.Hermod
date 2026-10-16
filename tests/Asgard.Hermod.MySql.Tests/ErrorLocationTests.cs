using Asgard.Hermod.Abstractions.Models;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>MySQL 未公开协议位置时，只定位已实际发送的失败语句起点，不解析服务器消息。</summary>
public sealed class ErrorLocationTests
{
    [Fact]
    public void BatchWhitespaceAndDelimiterKeepDocumentCoordinates()
    {
        const string sql = "SELECT 1;\r\n  SELECT invalid;"; var statement = MySqlSql.Split(sql)[1];
        var failure = new MySqlFailure("MySqlStatementRejected", "MYSQL_STATEMENT", 1064).AtStatement(sql, statement.Start, statement.Length, 2);
        Assert.Equal(new SqlErrorLocation(2, 13, 2, 3, false), failure.Location);
        const string routine = "DELIMITER $$\n  CREATE PROCEDURE broken() BEGIN SELECT invalid; END$$"; var procedure = Assert.Single(MySqlSql.Split(routine));
        var location = new MySqlFailure("MySqlStatementRejected", "MYSQL_STATEMENT", 1064).AtStatement(routine, procedure.Start, procedure.Length, 1).Location;
        Assert.Equal(routine.IndexOf("CREATE", StringComparison.Ordinal), location!.Offset); Assert.Equal(2, location.Line); Assert.False(location.IsExact);
    }
}
