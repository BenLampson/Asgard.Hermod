using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.MySql;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

public sealed class ProtocolTests
{
    [Theory]
    [InlineData("SELECT 'a;b'; SELECT 2", 2)]
    [InlineData("SELECT 'a\\';b'; SELECT 2", 2)]
    [InlineData("SELECT `a;b`, 'it''s;fine'; # ;\n SELECT 3", 2)]
    [InlineData("-- ; comment\n/* ; */ SELECT 1;", 1)]
    [InlineData("SELECT 1--2; SELECT 3", 2)]
    [InlineData("# comment\n/* comment */", 0)]
    [InlineData("/*!80000 SET @x=1 */; SELECT 2", 2)]
    public void Split_RespectsMySqlQuotedValuesAndComments(string sql, int count) => Assert.Equal(count, MySqlSql.Split(sql).Count);

    [Fact]
    public void Split_HandlesStoredRoutineDelimiterAndRetainsCaretRanges()
    {
        const string sql = "DELIMITER $$\nCREATE PROCEDURE p() BEGIN SELECT 'a;$$'; SELECT 2; END$$\nDELIMITER ;\nSELECT 3;";
        var parts = MySqlSql.Split(sql);
        Assert.Equal(2, parts.Count); Assert.Contains("SELECT 2;", parts[0].Sql); Assert.Equal("SELECT 3", parts[1].Sql);
        Assert.Equal(parts[0].Sql, sql.Substring(parts[0].Start, parts[0].Length).Trim());
    }

    [Theory]
    [InlineData("SELECT 'unclosed")]
    [InlineData("SELECT 1 /* unclosed")]
    [InlineData("SELECT 1\nDELIMITER $$")]
    public void Split_RejectsIncompleteSqlBeforeDispatch(string sql) => Assert.Throws<InvalidOperationException>(() => MySqlSql.Split(sql));

    [Fact]
    public void Split_RespectsNoBackslashEscapes() => Assert.Equal(2, MySqlSql.Split("SELECT 'a\\'; SELECT 2", true).Count);

    [Theory]
    [InlineData("SELECT id,name FROM app.users WHERE id=1", true)]
    [InlineData("SELECT `id`, `join` FROM `users`", true)]
    [InlineData("SELECT count(*) FROM users", false)]
    [InlineData("SELECT DISTINCT id FROM users", false)]
    [InlineData("SELECT a.id FROM users a JOIN users b ON a.id=b.id", false)]
    [InlineData("SELECT id FROM users, orders", false)]
    [InlineData("SELECT id+1 AS id FROM users", false)]
    [InlineData("SELECT id FROM users UNION SELECT id FROM users", false)]
    [InlineData("SELECT id FROM users /*!80000 JOIN orders */", false)]
    public void Editing_RejectsUnverifiableQueryShapes(string sql, bool editable) => Assert.Equal(editable, MySqlSql.IsSimpleProjection(sql));

    [Fact]
    public void IdentifierQuoting_DoesNotAllowSqlInjection() => Assert.Equal("`a``;DROP TABLE b`", MySqlSql.Quote("a`;DROP TABLE b"));
    [Fact]
    public void ProjectionSource_PreservesEscapedNamesAndNumericInputRejectsGrouping()
    {
        var source = MySqlSql.ProjectionSource("SELECT `id` FROM `app`.`a``b` WHERE id=1");
        Assert.Equal(("app", "a`b"), source);
        Assert.Throws<InvalidOperationException>(() => MySqlValues.Parse("1,25", new(0, "price", "DECIMAL", typeof(decimal), false), false));
    }

    [Fact]
    public void Options_DefaultToVerifiedTlsAndBoundedTimeouts()
    {
        var options = MySqlSessionOptions.Parse(Connection());
        Assert.Equal("verify-full", options.TlsMode); Assert.Equal(3306, options.Port); Assert.Equal(15, options.ConnectTimeout);
        Assert.DoesNotContain("very-secret", options.ToString());
    }

    [Theory]
    [InlineData("host", "host1,host2")]
    [InlineData("host", "https://host")]
    [InlineData("port", "0")]
    [InlineData("timeout", "0")]
    [InlineData("commandTimeout", "301")]
    [InlineData("tls", "preferred")]
    [InlineData("user", "")]
    public void Options_RejectUnsafeOrInvalidProtocolValues(string key, string value)
    {
        var connection = Connection(); var parameters = connection.Parameters.ToDictionary(); parameters[key] = value;
        Assert.Throws<InvalidOperationException>(() => MySqlSessionOptions.Parse(new() { TypeId = connection.TypeId, ProviderId = connection.ProviderId, Parameters = parameters }));
    }

    [Fact]
    public void Values_PreserveBinaryAndDistinguishNullFromEmpty()
    {
        var binary = new MySqlColumn(0, "blob", "BLOB", typeof(byte[]), true);
        var text = new MySqlColumn(1, "name", "VARCHAR", typeof(string), true);
        Assert.Equal(new byte[] { 0, 255, 128 }, MySqlValues.Parse("00FF80", binary, false));
        Assert.Equal(new byte[] { 0, 255, 128 }, MySqlValues.Parse("AP+A", binary, false, "base64"));
        Assert.Null(MySqlValues.Parse("NULL", text, true)); Assert.Equal("", MySqlValues.Parse("", text, false));
        Assert.Equal("NULL", MySqlValues.Parse("NULL", text, false));
        Assert.True(MySqlValues.Equal(new byte[] { 0, 1 }, new byte[] { 0, 1 }));
    }

    [Fact]
    public void Values_ParseNumbersInvariantAndRejectInvalidJson()
    {
        var old = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            Assert.Equal(1.25m, MySqlValues.Parse("1.25", new(0, "price", "DECIMAL", typeof(decimal), false), false));
            Assert.Throws<InvalidOperationException>(() => MySqlValues.Parse("{", new(0, "profile", "JSON", typeof(string), true), false));
            Assert.Throws<InvalidOperationException>(() => MySqlValues.Parse("NaN", new(0, "n", "DOUBLE", typeof(double), false), false));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public void SafeErrors_NeverExposeSdkMessagesAndResourcesExist(string culture)
    {
        var old = CultureInfo.CurrentUICulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var error = MySqlFailure.Safe(new IOException("password=very-secret; sql=customer-data"), true);
            string message = Text.Error(error);
            Assert.DoesNotContain("very-secret", message); Assert.DoesNotContain("customer-data", message);
            Assert.Equal("MYSQL_WRITE_UNKNOWN", error.Code);
            Assert.Contains(MySqlProfile.Fields(MySqlIds.Ssh), item => item.Key == "ssh.password" && item.Kind == InstanceFieldKind.Secret);
            Assert.Contains(MySqlProfile.Fields(MySqlIds.Ssh), item => item.Key == "password" && item.Kind == InstanceFieldKind.Secret);
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }

    private static InstanceConnectionOptions Connection() => new()
    {
        TypeId = MySqlIds.Type,
        ProviderId = MySqlIds.Direct,
        Parameters = new Dictionary<string, string> { ["host"] = "localhost", ["user"] = "reader", ["password"] = "very-secret" }
    };
}
