using System.Text.Json;
using Asgard.Hermod.RabbitMq;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Mcp.Tests;

public sealed class McpDiagnosticToolTests
{
    [Fact]
    public async Task SshUsesOnlyFixedDiagnosticsForTheGrantedService()
    {
        int commands = 0;
        var ssh = ProtocolStub<ISshConnectionFactory>.For((name, args) =>
        {
            Assert.Equal(nameof(ISshConnectionFactory.ExecuteDiagnosticAsync), name);
            commands++;
            Assert.Equal("LC_ALL=C systemctl show --no-pager -p LoadState,ActiveState,SubState -- 'exports.service'", args[1]);
            return Task.FromResult(new SshCommandResult("ActiveState=active", 0, false));
        });
        var target = new McpTarget("ssh", "test", "ssh", "1");
        using var rig = new McpTestRig([target], ssh: ssh);
        var grant = await rig.GrantAsync(target, "exports.service");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync("hermod_ssh_diagnose", grant, ("check", "rm -rf /"))).IsError);
        Assert.True((await rig.CallAsync("hermod_ssh_service_logs", grant)).IsError);
        Assert.Equal(0, commands);
        var result = (await rig.CallAsync("hermod_ssh_diagnose", grant, ("check", "service_status"))).StructuredContent!.Value;
        Assert.Equal("ActiveState=active", result.GetProperty("output").GetString());
        Assert.Equal(1, commands);
    }

    [Fact]
    public async Task RedisEscapesTheLiteralPrefixAndReturnsMetadataWithoutValues()
    {
        var redis = ProtocolStub<IRedisSession>.For((name, args) => name switch
        {
            "get_DatabaseCount" => 4,
            "get_ScanNodes" => new[] { "unlisted-node" },
            nameof(IRedisSession.ScanAsync) => Scan(args),
            nameof(IRedisSession.ReadKeyMetadataAsync) => Metadata(args),
            _ => throw new InvalidOperationException(name),
        });
        var target = new McpTarget("redis", "cache", "redis", "1");
        using var rig = new McpTestRig([target], redis: ProtocolStub<IRedisSessionFactory>.For((_, _) => Task.FromResult(redis)));
        var grant = await rig.GrantAsync(target, "2", "tenant[2]:");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        Assert.True((await rig.CallAsync("hermod_redis_key_info", grant, ("key", "tenant2:key"))).IsError);
        var keys = (await rig.CallAsync("hermod_redis_scan_keys", grant)).StructuredContent!.Value;
        Assert.Equal("tenant[2]:key", Assert.Single(keys.GetProperty("keys").EnumerateArray()).GetString());
        Assert.DoesNotContain("unlisted-node", keys.GetRawText());
        var key = (await rig.CallAsync("hermod_redis_key_info", grant, ("key", "tenant[2]:key"))).StructuredContent!.Value;
        Assert.Equal(9000, key.GetProperty("ttl_milliseconds").GetInt64());
        Assert.False(key.TryGetProperty("value", out _));

        static Task<RedisScanPage> Scan(object?[] args)
        {
            Assert.Equal(2, args[1]);
            Assert.Equal(@"tenant\[2\]:*", args[3]);
            return Task.FromResult(new RedisScanPage("0", [RedisKeyId.FromText("tenant[2]:key"), RedisKeyId.FromText("tenant2:key")]));
        }
        static Task<RedisKeyMetadata> Metadata(object?[] args)
        {
            Assert.Equal(2, args[0]);
            Assert.Equal("tenant[2]:key", Assert.IsType<RedisKeyId>(args[1]).DisplayName);
            return Task.FromResult(new RedisKeyMetadata("string", 9000));
        }
    }

    [Fact]
    public async Task RabbitMqReadsScopedQueueCountsAndDoesNotReturnRawPayloads()
    {
        var record = new RabbitMqRecord(JsonSerializer.SerializeToElement(new { name = "exports", vhost = "/test", messages_ready = 24, consumers = 1, payload = "private-content" }));
        var queues = ProtocolStub<IRabbitMqSession>.For((name, args) =>
        {
            Assert.Equal(nameof(IRabbitMqSession.ReadObjectAsync), name);
            Assert.Equal(RabbitMqResource.Queues, args[0]);
            Assert.Equal("/test", args[1]);
            Assert.Equal("exports", args[2]);
            return Task.FromResult(record);
        });
        var target = new McpTarget("mq", "queues", "rabbitmq", "1");
        using var rig = new McpTestRig([target], queues: ProtocolStub<IRabbitMqSessionFactory>.For((_, _) => Task.FromResult(queues)));
        var grant = await rig.GrantAsync(target, "/test");
        await rig.Access.SetEnabledAsync("", true, McpTestRig.Token);
        var result = (await rig.CallAsync("hermod_rabbitmq_queue_info", grant, ("queue", "exports"))).StructuredContent!.Value;
        Assert.Equal(24, result.GetProperty("queue").GetProperty("ready").GetDouble());
        Assert.DoesNotContain("private-content", result.GetRawText());
    }
}
