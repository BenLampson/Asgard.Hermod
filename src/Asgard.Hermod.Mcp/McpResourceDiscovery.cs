namespace Asgard.Hermod.Mcp;

/// <summary>按当前实例类型发现可选资源；返回前过滤用户禁止的项目。</summary>
internal sealed class McpResourceDiscovery(IMcpStorageScopeBrowser storage, McpSqlTools sql, McpRedisTools redis,
    McpRabbitMqTools queues, McpSshTools ssh)
{
    internal async Task<object> ListAsync(McpSessionAccess session, McpTarget target, InstanceConnectionOptions connection,
        McpArguments args, CancellationToken token)
    {
        bool allowed(string resource, string qualifier = "") => !McpExclusionRules.Blocks(session.Policy, target, resource, qualifier);
        switch (target.TypeId)
        {
            case "storage":
                var buckets = await storage.ListBucketsAsync(session.Policy.Owner, target, token).ConfigureAwait(false);
                var visible = buckets.Where(bucket => allowed(bucket.Name)).Select(bucket => bucket.Name).ToArray();
                int offset = args.Integer("offset", 0);
                string[] items = visible.Skip(offset).Take(100).ToArray();
                return new
                {
                    kind = "buckets",
                    items,
                    nextOffset = offset + items.Length < visible.Length && offset + items.Length <= 10000
                    ? (int?)(offset + items.Length) : null
                };
            case "mysql":
            case "postgresql":
                return await sql.ListResourcesAsync(target.TypeId, args.String("database"), args.Integer("offset", 0), connection, allowed, token).ConfigureAwait(false);
            case "redis":
                return await redis.ListDatabasesAsync(connection, resource => allowed(resource), token).ConfigureAwait(false);
            case "rabbitmq":
                return await queues.ListVhostsAsync(connection, args.Integer("page", 1), resource => allowed(resource), token).ConfigureAwait(false);
            case "ssh":
                return await ssh.ListServicesAsync(connection, resource => allowed(resource), token).ConfigureAwait(false);
            default: throw Text.Exception("McpAccessDenied");
        }
    }
}
