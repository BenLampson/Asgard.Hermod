using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using Npgsql;
using Xunit;

namespace Asgard.Hermod.PostgreSql.Tests;

/// <summary>仅面向明确指定的回环隔离 PostgreSQL；每次创建随机架构并清理。</summary>
public sealed class LivePostgreSqlTests
{
    public static bool Enabled => int.TryParse(Environment.GetEnvironmentVariable("HERMOD_POSTGRESQL_TEST_PORT"), out _);
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact(Skip = "Set HERMOD_POSTGRESQL_TEST_PORT for an isolated local server.", SkipUnless = nameof(Enabled))]
    public async Task Metadata_BrowsesSchemasRoutinesAndTypedTables()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var schemas = await session.ListAsync(new(PostgreSqlObjectKind.Database, "postgres", "postgres"), 0, Token);
        Assert.Contains(schemas.Items, item => item.Name == fixture.Schema && item.Schema == fixture.Schema);
        var tables = await session.ListAsync(new(PostgreSqlObjectKind.Tables, "", "postgres", Schema: fixture.Schema), 0, Token);
        Assert.Contains(tables.Items, item => item.Name == "users");
        var table = await session.DescribeAsync("postgres", fixture.Schema, "users", Token);
        Assert.True(table.SupportsEditing); Assert.Equal(typeof(int), table.Keys[0].ValueType);
        var functions = await session.ListAsync(new(PostgreSqlObjectKind.Functions, "", "postgres", Schema: fixture.Schema), 0, Token);
        var function = Assert.Single(functions.Items);
        Assert.Contains("CREATE OR REPLACE FUNCTION", await session.ReadDefinitionAsync(function, Token));
        var arguments = await session.ListAsync(new(PostgreSqlObjectKind.Parameters, "", "postgres", function.Name, Schema: fixture.Schema), 0, Token);
        Assert.Single(arguments.Items); Assert.Equal("value", arguments.Items[0].Name);
        var columns = await session.ListAsync(new(PostgreSqlObjectKind.Columns, "", "postgres", "users", Schema: fixture.Schema), 0, Token);
        Assert.Contains(columns.Items, item => item.Name == "profile" && item.Detail == "jsonb");
        string definition = await session.ReadDefinitionAsync(new(PostgreSqlObjectKind.Table, "users", "postgres", Schema: fixture.Schema), Token);
        Assert.Contains("GENERATED ALWAYS AS", definition); Assert.Contains("PRIMARY KEY", definition);
    }

    [Fact(Skip = "Set HERMOD_POSTGRESQL_TEST_PORT for an isolated local server.", SkipUnless = nameof(Enabled))]
    public async Task Queries_PreserveEarlierResultsDuplicateNamesAndIsolateSessions()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var first = await fixture.OpenAsync(); await using var second = await fixture.OpenAsync();
        var result = await first.ExecuteAsync("SELECT 1 AS duplicate,2 AS duplicate; SELECT * FROM hermod_missing_table", "postgres", Token);
        Assert.Single(result.Results); Assert.Equal("duplicate", result.Results[0].Columns[1].Name);
        Assert.Equal("POSTGRESQL_STATEMENT", result.Failure?.Code); Assert.Null(result.Results[0].EditableTable);
        Assert.Null((await first.ExecuteAsync("CREATE TEMP TABLE private_session(id int); INSERT INTO private_session VALUES(1)", "postgres", Token)).Failure);
        Assert.NotNull((await second.ExecuteAsync("SELECT * FROM private_session", "postgres", Token)).Failure);
        await Assert.ThrowsAsync<InvalidOperationException>(() => first.ExecuteAsync("SELECT 1", "other_database", Token));
    }

    [Fact(Skip = "Set HERMOD_POSTGRESQL_TEST_PORT for an isolated local server.", SkipUnless = nameof(Enabled))]
    public async Task Preview_CancelsLargeResultsStopsLaterStatementsAndCanResume()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        var preview = await session.ExecuteAsync("SELECT generate_series(1,2000); SELECT 123", "postgres", Token);
        Assert.Single(preview.Results); Assert.Equal(1000, preview.Results[0].Rows.Count);
        Assert.True(preview.Results[0].IsTruncated); Assert.Equal("POSTGRESQL_PREVIEW_LIMIT", preview.Failure?.Code);
        var large = await session.ExecuteAsync("SELECT repeat('x',11*1024*1024); SELECT 123", "postgres", Token);
        Assert.True(large.Results[0].IsTruncated); Assert.Empty(large.Results[0].Rows);
        var arrays = await session.ExecuteAsync("SELECT ARRAY[repeat('x',6*1024*1024),repeat('y',6*1024*1024)]", "postgres", Token);
        Assert.True(arrays.Results[0].IsTruncated); Assert.Empty(arrays.Results[0].Rows);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var cancelled = await session.ExecuteAsync("SELECT pg_sleep(10)", "postgres", cancellation.Token);
        Assert.Equal("POSTGRESQL_CANCELLED", cancelled.Failure?.Code);
        Assert.Null((await session.ExecuteAsync("SELECT 1", "postgres", Token)).Failure);
    }

    [Fact(Skip = "Set HERMOD_POSTGRESQL_TEST_PORT for an isolated local server.", SkipUnless = nameof(Enabled))]
    public async Task Plans_DoNotExecuteWritesAndPagingUsesLookahead()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var session = await fixture.OpenAsync();
        string tableName = PostgreSqlSql.Quote(fixture.Schema) + ".users";
        var plan = await session.ExplainAsync($"DELETE FROM {tableName}", "postgres", Token);
        Assert.Null(plan.Failure); Assert.Contains("Plan", Assert.IsType<string>(plan.Results[0].Rows[0][0]));
        var page = await session.ReadTableAsync("postgres", fixture.Schema, "users", 0, 50, "id > 0", Token);
        Assert.Equal(50, page.Rows.Count); Assert.True(page.HasMore); Assert.NotNull(page.EditableTable);
        var last = await session.ReadTableAsync("postgres", fixture.Schema, "users", 50, 50, "id > 0", Token);
        Assert.Equal(5, last.Rows.Count); Assert.False(last.HasMore);
    }

    [Fact(Skip = "Set HERMOD_POSTGRESQL_TEST_PORT for an isolated local server.", SkipUnless = nameof(Enabled))]
    public async Task Editing_UpdatesTypedValuesAndRollsBackWholeConflictingBatch()
    {
        await using var fixture = await Fixture.CreateAsync(); await using var writer = await fixture.OpenAsync();
        var table = await writer.DescribeAsync("postgres", fixture.Schema, "users", Token);
        var first = await writer.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 1 }, Token);
        var second = await writer.ReadRowAsync(table, new Dictionary<string, object?> { ["id"] = 2 }, Token);
        await writer.SaveAsync(table, [new(first, new Dictionary<string, object?> { ["profile"] = "{\"saved\":true}", ["payload"] = new byte[] { 0, 255 } })], Token);
        first = await writer.ReadRowAsync(table, first, Token);
        Assert.Equal(new byte[] { 0, 255 }, first["payload"]); Assert.Contains("saved", Assert.IsType<string>(first["profile"]));
        await using (var other = await fixture.OpenAsync())
            Assert.Null((await other.ExecuteAsync($"UPDATE {PostgreSqlSql.Quote(fixture.Schema)}.users SET name='server' WHERE id=2", "postgres", Token)).Failure);
        var draft = new Dictionary<string, object?> { ["name"] = "draft" };
        var conflict = await Assert.ThrowsAsync<PostgreSqlConflict>(() => writer.SaveAsync(table, [new(first, draft), new(second, draft)], Token));
        Assert.Equal("server", conflict.Current?["name"]);
        Assert.Equal(first["name"], (await writer.ReadRowAsync(table, first, Token))["name"]);
        await writer.ExecuteAsync("BEGIN", "postgres", Token);
        var failure = await Assert.ThrowsAsync<PostgreSqlFailure>(() => writer.SaveAsync(table, [new(first, draft)], Token));
        Assert.Equal("POSTGRESQL_WRITER_REQUIRED", failure.Code); await writer.RollbackAsync(Token);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Schema { get; } = "hermod_" + Guid.NewGuid().ToString("N");
        public PostgreSqlSessionFactory Factory { get; } = new(new NoSsh());
        private NpgsqlConnection _admin = null!;
        public InstanceConnectionOptions Options => new()
        {
            TypeId = PostgreSqlIds.Type,
            ProviderId = PostgreSqlIds.Direct,
            Parameters = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = Environment.GetEnvironmentVariable("HERMOD_POSTGRESQL_TEST_PORT")!, ["user"] = "postgres", ["database"] = "postgres", ["tls"] = "disabled" }
        };
        public Task<IPostgreSqlSession> OpenAsync() => Factory.OpenAsync(Options, Token);
        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            fixture._admin = new NpgsqlConnection($"Host=127.0.0.1;Port={Environment.GetEnvironmentVariable("HERMOD_POSTGRESQL_TEST_PORT")};Username=postgres;Database=postgres;SSL Mode=Disable;Pooling=false;GSS Encryption Mode=Disable");
            await fixture._admin.OpenAsync(Token);
            using var command = fixture._admin.CreateCommand(); string schema = PostgreSqlSql.Quote(fixture.Schema);
            command.CommandText = $"CREATE SCHEMA {schema}; CREATE TABLE {schema}.users(id integer PRIMARY KEY, name text, profile jsonb, payload bytea, computed integer GENERATED ALWAYS AS (id+1) STORED); INSERT INTO {schema}.users(id,name,profile) SELECT i,'name'||i,'{{}}'::jsonb FROM generate_series(1,55) i; CREATE FUNCTION {schema}.echo(value integer) RETURNS integer LANGUAGE sql AS $$ SELECT value $$";
            await command.ExecuteNonQueryAsync(Token); return fixture;
        }
        public async ValueTask DisposeAsync()
        {
            try { using var command = _admin.CreateCommand(); command.CommandText = $"DROP SCHEMA {PostgreSqlSql.Quote(Schema)} CASCADE"; await command.ExecuteNonQueryAsync(); }
            finally { await _admin.DisposeAsync(); }
        }
    }
    private sealed class NoSsh : ISshTcpStreamFactory
    {
        public Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
