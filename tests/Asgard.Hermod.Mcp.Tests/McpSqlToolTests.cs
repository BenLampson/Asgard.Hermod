using Asgard.Hermod.MySql;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpSqlToolTests
{
    [Fact]
    public async Task MySqlBuildsQualifiedReadOnlySampleAndRejectsAnotherTableOrSqlArgument()
    {
        int executed = 0;
        var columns = new[] { new MySqlColumn(0, "task_id", "bigint", typeof(long), false, PrimaryKey: true) };
        var session = ProtocolStub<IMySqlSession>.For((name, args) =>
        {
            if (name == nameof(IMySqlSession.DescribeAsync)) return Task.FromResult(new MySqlTable("orders", "jobs", columns, "InnoDB"));
            Assert.Equal(nameof(IMySqlSession.ExecuteReadOnlyAsync), name);
            executed++;
            Assert.Equal("orders", args[1]);
            Assert.Equal("SELECT * FROM `orders`.`jobs` ORDER BY `task_id` LIMIT 2 OFFSET 0", args[0]);
            var result = new MySqlResult(1, 1, columns, [[1L], [2L]]);
            return Task.FromResult(new MySqlExecution([result], [], null, TimeSpan.Zero, new(false, false)));
        });
        var target = new McpTarget("m", "MySQL", "mysql", "1");
        using var rig = new McpTestRig([target], mysql: ProtocolStub<IMySqlSessionFactory>.For((_, _) => Task.FromResult(session)));
        var grant = await rig.GrantAsync(target, "orders", table: "jobs", share: true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync("hermod_db_read_rows", grant, ("table", "accounts"))).IsError);
        Assert.True((await rig.CallAsync("hermod_db_read_rows", grant, ("table", "jobs"), ("sql", "DROP TABLE jobs"))).IsError);
        Assert.Equal(0, executed);
        var data = (await rig.CallAsync("hermod_db_read_rows", grant, ("table", "jobs"), ("limit", 1))).StructuredContent!.Value;
        Assert.Equal(1, Assert.Single(data.GetProperty("rows").EnumerateArray())[0].GetInt64());
        Assert.Equal(1, data.GetProperty("next_offset").GetInt32());
        Assert.Equal(1, executed);
    }

    [Fact]
    public async Task PostgreSqlUsesGrantedDatabaseAndSchemaAndDoesNotReadInheritedTables()
    {
        var columns = new[] { new PostgreSqlColumn(0, "attempt", "int", typeof(int), false) };
        var session = ProtocolStub<IPostgreSqlSession>.For((name, args) =>
        {
            if (name == nameof(IPostgreSqlSession.DescribeAsync))
            {
                Assert.Equal("reports", args[0]);
                Assert.Equal("exports", args[1]);
                return Task.FromResult(new PostgreSqlTable("reports", "jobs", columns, "r", "exports"));
            }
            Assert.Equal(nameof(IPostgreSqlSession.ExecuteReadOnlyAsync), name);
            Assert.Equal("SELECT * FROM ONLY \"exports\".\"jobs\" LIMIT 11 OFFSET 0", args[0]);
            var result = new PostgreSqlResult(1, 1, columns, [[3]]);
            return Task.FromResult(new PostgreSqlExecution([result], [], null, TimeSpan.Zero, new(false, false)));
        });
        var factory = ProtocolStub<IPostgreSqlSessionFactory>.For((_, args) =>
        {
            var effective = Assert.IsType<InstanceConnectionOptions>(args[0]);
            Assert.Equal("reports", effective.Parameters["database"]);
            Assert.Equal("postgresql.test", effective.ProviderId);
            return Task.FromResult(session);
        });
        var target = new McpTarget("pg", "PostgreSQL", "postgresql", "1");
        using var rig = new McpTestRig([target], postgres: factory);
        var grant = await rig.GrantAsync(target, "reports", "exports", "jobs", true);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var data = (await rig.CallAsync("hermod_db_read_rows", grant, ("table", "jobs"))).StructuredContent!.Value;
        Assert.Equal(3, Assert.Single(data.GetProperty("rows").EnumerateArray())[0].GetInt32());
    }
}
