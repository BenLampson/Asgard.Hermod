using Asgard.Hermod.ObjectStorage.Abstractions.Clients;

namespace Asgard.Hermod.Mcp;

/// <summary>读取和传输共用按连接版本及 Bucket 缓存的地域，客户端仍为每个操作独立创建。</summary>
internal sealed class McpStorageClients(IObjectStorageClientFactory factory)
{
    private const int MaximumRoutes = 128;
    private const long RouteLifetimeMs = 10 * 60 * 1000;
    private readonly Dictionary<(string InstanceId, string Revision, string Bucket), (string Region, long Stamp)> _routes = [];

    internal async Task<IObjectStorageClient> CreateAsync(McpGrant grant, InstanceConnectionOptions connection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = (grant.InstanceId, grant.Revision, grant.Resource);
        string? known = null;
        lock (_routes)
        {
            if (_routes.TryGetValue(key, out var route))
            {
                if (Environment.TickCount64 - route.Stamp < RouteLifetimeMs) known = route.Region;
                else _routes.Remove(key);
            }
        }
        if (known is not null) return factory.CreateClient(connection, known);

        var initial = factory.CreateClient(connection);
        if (initial is not IObjectStorageBucketRegionClient lookup) return initial;

        bool keepInitial = false;
        try
        {
            string? region = await lookup.FindBucketRegionAsync(grant.Resource, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (string.IsNullOrEmpty(region))
            {
                keepInitial = true;
                return initial;
            }

            var routed = factory.CreateClient(connection, region);
            lock (_routes)
            {
                if (_routes.Count >= MaximumRoutes && !_routes.ContainsKey(key))
                    _routes.Remove(_routes.MinBy(item => item.Value.Stamp).Key);
                _routes[key] = (region, Environment.TickCount64);
            }
            return routed;
        }
        finally
        {
            if (!keepInitial)
            {
                try { initial.Dispose(); }
                catch (Exception) { /* 探测连接的清理不影响真正的请求。 */ }
            }
        }
    }
}
