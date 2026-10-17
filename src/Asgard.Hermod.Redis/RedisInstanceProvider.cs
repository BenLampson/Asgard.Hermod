namespace Asgard.Hermod.Redis;

/// <summary>只读验证实际连接及 SCAN 权限，不写入任何业务数据。</summary>
public sealed class RedisInstanceProvider(string topology, IRedisSessionFactory factory) : IInstanceProvider
{
    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; } = RedisProfile.Descriptor(topology);
    /// <inheritdoc />
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        if (options.ProviderId != topology) throw Text.Exception("RedisConfigurationInvalid");
        try
        {
            await using var session = await factory.OpenAsync(options, cancellationToken).ConfigureAwait(false);
            var checks = new List<InstanceConnectionCheck> { new(Text.Get("RedisConnectionVerified"), true), new(Text.Get("RedisTopologyVerified"), true) };
            bool browse = true;
            foreach (string node in session.ScanNodes)
                try { await session.ScanAsync(node, session.Options.Database, "0", "*", "", cancellationToken).ConfigureAwait(false); }
                catch (RedisException error) when (error.Code == "REDIS_PERMISSION") { browse = false; }
            checks.Add(new(Text.Get("RedisScanVerified"), browse));
            return new(true, Text.Get(browse ? "RedisTestPassed" : "RedisTestBrowseLimited"), Checks: checks);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { var safe = RedisException.Safe(error); return new(false, Text.Error(safe, "RedisConnectionFailed"), safe.Code); }
    }
}
