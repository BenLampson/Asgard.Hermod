using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.Redis;

/// <summary>无后台命令积压或写入重放；Sentinel 发现与数据连接使用独立配置。</summary>
public sealed class RedisSessionFactory(ISshTcpStreamFactory ssh) : IRedisSessionFactory
{
    /// <inheritdoc />
    public async Task<IRedisSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var profile = RedisSessionOptions.Parse(options);
        var owned = new List<IDisposable>();
        ConnectionMultiplexer? connection = null;
        try
        {
            var data = Configure("");
            if (profile.Topology == RedisIds.Sentinel)
            {
                var sentinel = Configure("sentinel.");
                sentinel.CommandMap = CommandMap.Sentinel; sentinel.DefaultDatabase = null;
                var discovery = await CompleteAsync(ConnectionMultiplexer.SentinelConnectAsync(sentinel), cancellationToken).ConfigureAwait(false);
                owned.Add(discovery); data.ServiceName = profile.Value("service");
                connection = await CompleteAsync(Task.Run(() => discovery.GetSentinelMasterConnection(data), CancellationToken.None), cancellationToken).ConfigureAwait(false);
            }
            else connection = await CompleteAsync(ConnectionMultiplexer.ConnectAsync(data), cancellationToken).ConfigureAwait(false);
            var servers = connection.GetServers().Where(server => !server.IsReplica && server.ServerType != ServerType.Sentinel).ToArray();
            if (servers.Length == 0 || servers.Any(server => !server.IsConnected)) throw new RedisException("RedisTopologyFailed", "REDIS_TOPOLOGY");
            if (profile.Topology == RedisIds.Cluster && servers.Any(server => server.ServerType != ServerType.Cluster || server.ClusterConfiguration is null))
                throw new RedisException("RedisTopologyFailed", "REDIS_TOPOLOGY");
            if (profile.Topology == RedisIds.Cluster)
            {
                var required = servers[0].ClusterConfiguration!.Nodes.Where(node => !node.IsReplica && node.Slots.Count != 0).ToArray();
                if (required.Length == 0 || required.Any(node => node.IsFail || node.IsPossiblyFail || !connection.GetServer(node.EndPoint).IsConnected))
                    throw new RedisException("RedisTopologyFailed", "REDIS_TOPOLOGY");
                servers = required.Select(node => connection.GetServer(node.EndPoint)).ToArray();
            }
            // 安全 CAS 使用 Redis 7 的 PEXPIRETIME / ACL 检查，老版本不能降级成盲写。
            if (servers.Any(server => server.Version.Major < 7)) throw new RedisException("RedisVersionRequired", "REDIS_VERSION");
            await connection.GetDatabase(profile.Database).PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            return new RedisSession(connection, profile, servers, owned);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { connection?.Dispose(); owned.ForEach(item => item.Dispose()); throw; }
        catch (Exception error)
        { connection?.Dispose(); owned.ForEach(item => item.Dispose()); throw RedisException.Safe(error); }

        ConfigurationOptions Configure(string prefix)
        {
            var configuration = new ConfigurationOptions
            {
                AbortOnConnectFail = true,
                ConnectRetry = 0,
                ConnectTimeout = profile.TimeoutSeconds * 1000,
                AsyncTimeout = profile.TimeoutSeconds * 1000,
                SyncTimeout = profile.TimeoutSeconds * 1000,
                DefaultDatabase = profile.Database,
                BacklogPolicy = BacklogPolicy.FailFast,
                IncludeDetailInExceptions = false,
                IncludePerformanceCountersInExceptions = false,
                ClientName = "Hermod",
                TieBreaker = "",
                ConfigurationChannel = "",
                User = profile.Value(prefix + "auth", "none") == "acl" ? profile.Value(prefix + "user") : null,
                Password = profile.Value(prefix + "auth", "none") switch { "acl" => profile.Value(prefix + "acl.password"), "password" => profile.Value(prefix + "password"), _ => null },
                Tunnel = profile.Route == "direct" ? null : new RedisTunnel(profile, ssh, cancellationToken),
            };
            foreach (var endpoint in profile.Endpoints) configuration.EndPoints.Add(endpoint.ToEndPoint());
            RedisTls.Configure(configuration, profile, prefix, owned); return configuration;
        }
    }

    /// <summary>SDK 连接不接收取消令牌，取消后仍等待并回收迟到的连接。</summary>
    private static async Task<ConnectionMultiplexer> CompleteAsync(Task<ConnectionMultiplexer> pending, CancellationToken token)
    {
        try { return await pending.WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        { _ = pending.ContinueWith(task => { if (task.IsCompletedSuccessfully) task.Result.Dispose(); _ = task.Exception; }, TaskScheduler.Default); throw; }
    }
}
