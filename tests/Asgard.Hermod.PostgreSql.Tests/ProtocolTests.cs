using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Abstractions.Models;
using Npgsql;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>验证协议边界、类型转换、参数化筛选和安全错误，不连接数据库。</summary>
public sealed class ProtocolTests
{
    [Fact]
    public void Options_DefaultToVerifiedTlsPostgresAndBoundedTimeouts()
    {
        var options = PostgreSqlSessionOptions.Parse(Connection());
        Assert.Equal("verify-full", options.TlsMode); Assert.Equal(5432, options.Port);
        Assert.Equal("postgres", options.Database); Assert.Equal(15, options.ConnectTimeout);
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
    public void Options_RejectUnsafeValues(string key, string value)
    {
        var parameters = Connection().Parameters.ToDictionary(); parameters[key] = value;
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSessionOptions.Parse(new() { TypeId = PostgreSqlIds.Type, ProviderId = PostgreSqlIds.Direct, Parameters = parameters }));
    }
    [Fact]
    public void Values_PreserveBinaryUuidNullAndInvariantNumbers()
    {
        var old = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            Assert.Equal(new byte[] { 0, 255 }, PostgreSqlValues.Parse("00FF", new(0, "data", "bytea", typeof(byte[]), true), false));
            Assert.Equal(1.25m, PostgreSqlValues.Parse("1.25", new(0, "price", "numeric", typeof(decimal), false), false));
            Assert.Null(PostgreSqlValues.Parse("", new(0, "name", "text", typeof(string), true), true));
            Assert.Equal("", PostgreSqlValues.Parse("", new(0, "name", "text", typeof(string), true), false));
            var id = Guid.NewGuid(); Assert.Equal(id, PostgreSqlValues.Parse(id.ToString(), new(0, "id", "uuid", typeof(Guid), false), false));
            Assert.Throws<InvalidOperationException>(() => PostgreSqlValues.Parse("{", new(0, "json", "jsonb", typeof(string), true), false));
            Assert.Throws<InvalidOperationException>(() => PostgreSqlValues.Parse("1,25", new(0, "price", "numeric", typeof(decimal), false), false));
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
    [Fact]
    public void TablePlan_QuotesSchemaAndParameterizesValuesAndLookahead()
    {
        var plan = PostgreSqlTableReadPlan.Create(Table(), 100, 50, "\"MixedCase\" = 'a'';DROP TABLE users' AND id IN (1,2)");
        Assert.Contains("\"tenant\"\"one\".\"users\"", plan.Sql);
        Assert.DoesNotContain("DROP", plan.Sql); Assert.Contains("a';DROP TABLE users", plan.Parameters.Values);
        Assert.Equal(51, plan.Parameters["@limit"]); Assert.Equal(100, plan.Parameters["@offset"]);
    }
    [Theory]
    [InlineData("id=1; DELETE FROM users")]
    [InlineData("id IN (SELECT id FROM users)")]
    [InlineData("lower(id)=1")]
    [InlineData("missing=1")]
    [InlineData("id=1 --comment")]
    public void TablePlan_RejectsExecutableFilters(string filter) => Assert.Throws<InvalidOperationException>(() => PostgreSqlTableReadPlan.Create(Table(), 0, 200, filter));
    [Fact]
    public void Editing_RequiresOrdinaryBaseTablePrimaryKeyAndUpdatePermission()
    {
        Assert.True(Table().SupportsEditing); Assert.False((Table() with { RelationKind = "v" }).SupportsEditing);
        Assert.False((Table() with { RelationKind = "f" }).SupportsEditing);
        Assert.False((Table() with { Columns = Table().Columns.Select(c => c with { PrimaryKey = false }).ToArray() }).SupportsEditing);
    }
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public void Errors_DiscardServerDetailsAndKeepSqlState(string culture)
    {
        var old = CultureInfo.CurrentUICulture; CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var error = PostgreSqlFailure.Safe(new PostgresException("password=very-secret; SELECT customer", "ERROR", "ERROR", "23505"));
            Assert.Equal("POSTGRESQL_STATEMENT", error.Code); Assert.Contains("23505", error.ResourceArguments);
            Assert.DoesNotContain("very-secret", Text.Error(error));
            var network = PostgreSqlFailure.Safe(new IOException("secret"), true);
            Assert.Equal("POSTGRESQL_WRITE_UNKNOWN", network.Code);
            Assert.Contains(PostgreSqlProfile.Fields(PostgreSqlIds.Ssh), f => f.Key == "ssh.password" && f.Kind == InstanceFieldKind.Secret);
        }
        finally { CultureInfo.CurrentUICulture = old; }
    }
    private static PostgreSqlTable Table() => new("postgres", "users", [new(0, "id", "integer", typeof(int), false, PrimaryKey: true, CanUpdate: true), new(1, "MixedCase", "text", typeof(string), true, CanUpdate: true)], "r", "tenant\"one");
    [Fact]
    public void Arrays_AreReadablePreserveShapeAndCountPayloadBytes()
    {
        Assert.Equal("[1,2]", PostgreSqlValues.Format(new[] { 1, 2 }));
        Assert.Equal("[\"a\",null,\"long\"]", PostgreSqlValues.Format(new[] { "a", null, "long" }));
        Assert.Equal("[[1,2],[3,4]]", PostgreSqlValues.Format(new[,] { { 1, 2 }, { 3, 4 } }));
        Assert.Equal(64, PostgreSqlValues.Bytes(new[] { 1, 2 }));
        Assert.Equal(5, PostgreSqlValues.Bytes(new[] { "a", "long" }));
        Assert.False(PostgreSqlValues.SupportsInput(typeof(string[])));
        Assert.False((Table() with { Columns = [.. Table().Columns, new(2, "tags", "text[]", typeof(string[]), true)] }).SupportsEditing);
    }
    private static InstanceConnectionOptions Connection() => new()
    {
        TypeId = PostgreSqlIds.Type,
        ProviderId = PostgreSqlIds.Direct,
        Parameters = new Dictionary<string, string> { ["host"] = "localhost", ["user"] = "reader", ["password"] = "very-secret" }
    };
}
