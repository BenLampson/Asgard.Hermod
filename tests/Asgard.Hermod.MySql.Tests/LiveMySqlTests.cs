using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.MySql;
using Asgard.Hermod.Ssh;
using MySqlConnector;
using Xunit;

namespace Asgard.Hermod.MySql.Tests;

/// <summary>可选的真实 MySQL 集成验证，只在明确指定的本机测试服务器运行。</summary>
public sealed class LiveMySqlTests
{
    public static bool Enabled => int.TryParse(Environment.GetEnvironmentVariable("HERMOD_MYSQL_TEST_PORT"), out _);

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT to run against an isolated local MySQL test server.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_BrowsesTypedObjectsAndRetainsEarlierResultsOnFailure()
    {
        await using var fixture = await Fixture.CreateAsync();
        await using var session = await fixture.OpenAsync();
        var databases = await session.ListAsync(new(MySqlObjectKind.Root, ""), 0, TestContext.Current.CancellationToken);
        Assert.Contains(databases.Items, item => item.Name == fixture.Database);
        var tables = await session.ListAsync(new(MySqlObjectKind.Tables, "", fixture.Database), 0, TestContext.Current.CancellationToken);
        Assert.Contains(tables.Items, item => item.Name == "users");
        var result = await session.ExecuteAsync("SELECT id,name,profile FROM users; SELECT 1 AS duplicate, 2 AS duplicate; SELECT * FROM missing_table", fixture.Database, TestContext.Current.CancellationToken);
        Assert.Equal(2, result.Results.Count); Assert.Equal("MYSQL_STATEMENT", result.Failure?.Code);
        Assert.NotNull(result.Results[0].EditableTable); Assert.Equal("duplicate", result.Results[1].Columns[1].Name);
        Assert.Equal("json", result.Results[0].Columns[2].TypeName, ignoreCase: true);
        Assert.False(result.Outcomes[^1].Succeeded);
        var aggregate = await session.ExecuteAsync("SELECT count(*) FROM users", fixture.Database, TestContext.Current.CancellationToken);
        Assert.Null(aggregate.Results[0].EditableTable);
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT to run against an isolated local MySQL test server.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_UpdatesOnlyChangedFieldsWithTypedBaselineAndNull()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var result = await session.ReadTableAsync(fixture.Database, "users", 0, TestContext.Current.CancellationToken); var table = Assert.IsType<MySqlTable>(result.EditableTable);
        Assert.True(table.Columns.Single(column => column.Name == "created").CanUpdate);
        Assert.False(table.Columns.Single(column => column.Name == "computed").CanUpdate && !table.Columns.Single(column => column.Name == "computed").Generated);
        var original = await session.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 1 }, TestContext.Current.CancellationToken);
        await session.SaveAsync(table, [new(original, new Dictionary<string, object?> { ["name"] = "changed", ["payload"] = new byte[] { 0, 255, 128 }, ["profile"] = null })], TestContext.Current.CancellationToken);
        var current = await session.ReadRowAsync(table, original, TestContext.Current.CancellationToken);
        Assert.Equal("changed", current["name"]); Assert.Null(current["profile"]); Assert.Equal(new byte[] { 0, 255, 128 }, current["payload"]);
        Assert.Throws<InvalidOperationException>(() => MySqlValues.Parse("bad", table.Columns.First(column => column.Name == "profile"), false));
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT to run against an isolated local MySQL test server.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_ConflictingBatchRollsBackEarlierRowsAndPreservesDraft()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var table = await session.DescribeAsync(fixture.Database, "users", TestContext.Current.CancellationToken);
        var first = await session.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 1 }, TestContext.Current.CancellationToken);
        var second = await session.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 2 }, TestContext.Current.CancellationToken);
        await using (var other = await fixture.OpenAsync()) Assert.Null((await other.ExecuteAsync("UPDATE users SET name='server' WHERE id=2", fixture.Database, TestContext.Current.CancellationToken)).Failure);
        var draft = new Dictionary<string, object?> { ["name"] = "draft" };
        var conflict = await Assert.ThrowsAsync<MySqlConflict>(() => session.SaveAsync(table, [new(first, draft), new(second, draft)], TestContext.Current.CancellationToken));
        Assert.Equal("server", conflict.Current?["name"]); Assert.Equal("draft", conflict.Change.Changes["name"]);
        Assert.Equal(first["name"], (await session.ReadRowAsync(table, first, TestContext.Current.CancellationToken))["name"]);
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT to run against an isolated local MySQL test server.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_BoundsPreviewAndCancellationLeavesSessionUsable()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var result = await session.ExecuteAsync("SELECT a.TABLE_NAME FROM information_schema.COLUMNS a CROSS JOIN information_schema.COLUMNS b; SELECT 123", fixture.Database, TestContext.Current.CancellationToken);
        Assert.True(result.Results[0].IsTruncated); Assert.Equal(1000, result.Results[0].Rows.Count);
        Assert.Equal("MYSQL_PREVIEW_LIMIT", result.Failure?.Code); Assert.Single(result.Outcomes);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var cancelled = await session.ExecuteAsync("SELECT SLEEP(10)", fixture.Database, cancellation.Token);
        Assert.NotNull(cancelled.Failure);
        Assert.Null((await session.ExecuteAsync("SELECT 1", fixture.Database, TestContext.Current.CancellationToken)).Failure);
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT to run against an isolated local MySQL test server.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_RejectsUntrustedTlsInsteadOfFallingBackToPlaintext()
    {
        await using var fixture = await Fixture.CreateAsync();
        var parameters = fixture.Options.Parameters.ToDictionary(); parameters["tls"] = "verify-full";
        var error = await Assert.ThrowsAsync<MySqlFailure>(() => fixture.Factory.OpenAsync(new() { TypeId = MySqlIds.Type, ProviderId = MySqlIds.Direct, Parameters = parameters }, TestContext.Current.CancellationToken));
        Assert.DoesNotContain("password", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(Skip = "Set HERMOD_MYSQL_TEST_PORT for local integration checks.", SkipUnless = nameof(Enabled))]
    public async Task RealServer_GridSaveCannotImplicitlyCommitUserSqlTransaction()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var table = await session.DescribeAsync(fixture.Database, "users", TestContext.Current.CancellationToken);
        var original = await session.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 1 }, TestContext.Current.CancellationToken);
        Assert.Null((await session.ExecuteAsync("START TRANSACTION; UPDATE users SET name='pending' WHERE id=1", fixture.Database, TestContext.Current.CancellationToken)).Failure);
        var failure = await Assert.ThrowsAsync<MySqlFailure>(() => session.SaveAsync(table, [new(original, new Dictionary<string, object?> { ["name"] = "grid" })], TestContext.Current.CancellationToken));
        Assert.Equal("MYSQL_WRITER_REQUIRED", failure.Code);
        await using var other = await fixture.OpenAsync();
        Assert.Equal("one", (await other.ReadRowAsync(table, original, TestContext.Current.CancellationToken))["name"]);
        await session.RollbackAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Database { get; } = "hermod_test_" + Guid.NewGuid().ToString("N");
        public MySqlSessionFactory Factory { get; } = new(new NoSsh());
        public InstanceConnectionOptions Options => new()
        {
            TypeId = MySqlIds.Type,
            ProviderId = MySqlIds.Direct,
            Parameters = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = Environment.GetEnvironmentVariable("HERMOD_MYSQL_TEST_PORT")!, ["user"] = "root", ["tls"] = "required" }
        };
        public Task<IMySqlSession> OpenAsync() => Factory.OpenAsync(Options);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture(); await using var session = await fixture.OpenAsync();
            var result = await session.ExecuteAsync($"CREATE DATABASE {MySqlSql.Quote(fixture.Database)}; USE {MySqlSql.Quote(fixture.Database)}; CREATE TABLE users(id INT PRIMARY KEY, name VARCHAR(100), profile JSON, payload BLOB, created TIMESTAMP DEFAULT CURRENT_TIMESTAMP, computed INT GENERATED ALWAYS AS (id+1) STORED) ENGINE=InnoDB; INSERT INTO users(id,name,profile) VALUES(1,'one','{{\"a\":1}}'),(2,'two','{{\"a\":2}}')", "");
            Assert.Null(result.Failure); return fixture;
        }
        public async ValueTask DisposeAsync() { await using var session = await OpenAsync(); await session.ExecuteAsync($"DROP DATABASE {MySqlSql.Quote(Database)}", ""); }
    }
    private sealed class NoSsh : ISshTcpStreamFactory
    { public Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default) => throw new NotSupportedException(); }
}
