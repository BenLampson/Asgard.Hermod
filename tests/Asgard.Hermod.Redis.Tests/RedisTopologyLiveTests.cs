using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>显式启用本机三主节点 Cluster 与独立密码 Sentinel；不接触外部服务器。</summary>
public sealed class RedisTopologyLiveTests
{
    public static bool IsEnabled => Environment.GetEnvironmentVariable("HERMOD_REDIS_TOPOLOGY_TESTS") == "1";

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Configure isolated Sentinel and Cluster fixtures.")]
    public async Task Sentinel_UsesIndependentAuthenticationAndDiscoversPrimary()
    {
        var options = new InstanceConnectionOptions
        {
            TypeId = RedisIds.Type,
            ProviderId = RedisIds.Sentinel,
            Parameters = new Dictionary<string, string>
            { ["nodes"] = "127.0.0.1:16401", ["service"] = "hermod-primary", ["sentinel.auth"] = "password", ["sentinel.password"] = "isolated-sentinel-only" }
        };
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(options, TestContext.Current.CancellationToken);
        Assert.Single(session.ScanNodes); Assert.Contains("16400", session.ScanNodes[0]);
        var result = await new RedisInstanceProvider(RedisIds.Sentinel, new RedisSessionFactory(new UnusedSsh())).TestConnectionAsync(options, TestContext.Current.CancellationToken);
        Assert.True(result.IsSuccessful);
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Configure isolated Sentinel and Cluster fixtures.")]
    public async Task Cluster_DiscoversEveryPrimaryAndRoutesKeyWrites()
    {
        var token = TestContext.Current.CancellationToken;
        var options = new InstanceConnectionOptions { TypeId = RedisIds.Type, ProviderId = RedisIds.Cluster, Parameters = new Dictionary<string, string> { ["nodes"] = "127.0.0.1:16410" } };
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(options, token);
        Assert.Equal(3, session.ScanNodes.Count);
        var key = RedisKeyId.FromText("hermod-cluster-test:" + Guid.NewGuid().ToString("N"));
        try
        {
            var draft = new RedisSnapshot("string", 5, -1, [new([], "value"u8.ToArray())], true);
            await session.SaveAsync(0, key, RedisSnapshot.Missing, draft, token);
            var read = await session.ReadEditableAsync(0, key, token); Assert.Equal("value"u8.ToArray(), Assert.Single(read.Entries).Value);
            int discovered = 0; foreach (string node in session.ScanNodes)
            {
                string cursor = "0"; do { var page = await session.ScanAsync(node, 0, cursor, "hermod-cluster-test:*", "", token); cursor = page.Cursor; discovered += page.Keys.Count(item => item.Equals(key)); } while (cursor != "0");
            }
            Assert.Equal(1, discovered);
        }
        finally { var baseline = await session.ReadEditableAsync(0, key, token); await session.DeleteAsync(0, key, baseline, token); }
    }

    private sealed class UnusedSsh : ISshTcpStreamFactory
    {
        public Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
