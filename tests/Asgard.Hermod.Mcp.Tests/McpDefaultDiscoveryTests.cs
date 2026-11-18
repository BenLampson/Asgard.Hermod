using System.Text.Json;
using Asgard.Hermod.MySql;
using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.RabbitMq;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Ssh;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpDefaultDiscoveryTests
{
    [Fact]
    public async Task DiscoversDatabaseSchemasRedisHostsAndServicesWhileHidingProhibitedResources()
    {
        var mysql = new McpTarget("mysql", "MySQL", "mysql", "v1");
        var postgres = new McpTarget("postgres", "PostgreSQL", "postgresql", "v1");
        var redis = new McpTarget("redis", "Redis", "redis", "v1");
        var rabbit = new McpTarget("rabbit", "RabbitMQ", "rabbitmq", "v1");
        var ssh = new McpTarget("ssh", "SSH", "ssh", "v1");
        var mySession = ProtocolStub<IMySqlSession>.For((name, _) => name == nameof(IMySqlSession.ListAsync)
            ? Task.FromResult(new MySqlObjectPage([new(MySqlObjectKind.Database, "public"), new(MySqlObjectKind.Database, "private")], false))
            : throw new InvalidOperationException(name));
        var pgSession = ProtocolStub<IPostgreSqlSession>.For((name, args) =>
        {
            Assert.Equal(nameof(IPostgreSqlSession.ListAsync), name);
            var parent = Assert.IsType<PostgreSqlObject>(args[0]);
            return Task.FromResult(parent.Kind == PostgreSqlObjectKind.Root
                ? new PostgreSqlObjectPage([new(PostgreSqlObjectKind.Database, "main")], false)
                : new PostgreSqlObjectPage([new(PostgreSqlObjectKind.Schema, "public"), new(PostgreSqlObjectKind.Schema, "private")], false));
        });
        var redisSession = ProtocolStub<IRedisSession>.For((name, _) => name == "get_DatabaseCount" ? 3 : throw new InvalidOperationException(name));
        var rabbitSession = ProtocolStub<IRabbitMqSession>.For((name, _) => name == nameof(IRabbitMqSession.ReadPageAsync)
            ? Task.FromResult(new RabbitMqPage([Record("public"), Record("private")], 1, 50, 2)) : throw new InvalidOperationException(name));
        var prompt = new Allow();
        using var rig = new McpTestRig([mysql, postgres, redis, rabbit, ssh],
            mysql: ProtocolStub<IMySqlSessionFactory>.For((_, _) => Task.FromResult(mySession)),
            postgres: ProtocolStub<IPostgreSqlSessionFactory>.For((_, _) => Task.FromResult(pgSession)),
            redis: ProtocolStub<IRedisSessionFactory>.For((_, _) => Task.FromResult(redisSession)),
            queues: ProtocolStub<IRabbitMqSessionFactory>.For((_, _) => Task.FromResult(rabbitSession)),
            ssh: ProtocolStub<ISshConnectionFactory>.For((name, _) => name == nameof(ISshConnectionFactory.ExecuteDiagnosticAsync)
                ? Task.FromResult(new SshCommandResult("public.service enabled\nprivate.service enabled\n", 0, false))
                : throw new InvalidOperationException(name)), defaultAccess: true, approval: prompt);
        await rig.Access.SetExclusionsAsync("", [new("", mysql.Id, mysql.TypeId, "private"),
            new("", postgres.Id, postgres.TypeId, "main", "private"), new("", redis.Id, redis.TypeId, "1"),
            new("", rabbit.Id, rabbit.TypeId, "private"), new("", ssh.Id, ssh.TypeId, "private.service")], McpTestRig.Token);
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var session = rig.Session;
        Assert.Equal(["public"], await Names(mysql));
        Assert.Equal(["main"], await Names(postgres));
        Assert.Equal(["public"], await Names(postgres, "main"));
        Assert.Equal(["0", "2"], await Names(redis));
        Assert.Equal(["public"], await Names(rabbit));
        Assert.Equal(["public.service"], await Names(ssh));
        Assert.Equal(5, prompt.Count);

        async Task<string[]> Names(McpTarget target, string? database = null)
        {
            var args = new Dictionary<string, JsonElement> { ["grant_id"] = JsonSerializer.SerializeToElement(McpDefaultAccess.Id(session, target)) };
            if (database is not null) args["database"] = JsonSerializer.SerializeToElement(database);
            var result = await rig.Tools.InvokeAsync(session, McpToolCatalog.ListResources, args, rig.Cursors, McpTestRig.Token);
            Assert.False(result.IsError == true, string.Join(" ", result.Content.OfType<TextContentBlock>().Select(item => item.Text)));
            return result.StructuredContent!.Value.GetProperty("items").EnumerateArray().Select(item => item.ToString()).ToArray();
        }
        static RabbitMqRecord Record(string name) => new(JsonSerializer.SerializeToElement(new { name }));
    }

    private sealed class Allow : IMcpApprovalService
    {
        internal int Count { get; private set; }
        public Task<McpApprovalResult> ConfirmAsync(McpApprovalRequest request, CancellationToken cancellationToken = default)
        { Count++; return Task.FromResult(new McpApprovalResult(true)); }
    }
}
