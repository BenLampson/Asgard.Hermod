namespace Asgard.Hermod.Mcp;

/// <summary>在打开连接前检查禁止范围；递归传输、统计和删除不能越过任何子级禁止规则。</summary>
internal static class McpRequestAuthorization
{
    internal static void Check(McpSessionAccess session, McpTarget target, McpGrant grant, string tool,
        McpArguments args, McpCursorCache cursors)
    {
        bool blocked(string qualifier = "", string table = "", bool descendants = false) =>
            McpExclusionRules.Blocks(session.Policy, target, grant.Resource, qualifier, table, descendants);
        if (tool == McpToolCatalog.ListResources)
        {
            string database = args.String("database");
            if (McpExclusionRules.Blocks(session.Policy, target, database)) throw Text.Exception("McpAccessDenied");
            return;
        }
        bool denied = target.TypeId switch
        {
            "storage" => StorageDenied(),
            "redis" => blocked(tool == "hermod_redis_scan_keys" ? args.String("prefix") : args.String("key")),
            "mysql" or "postgresql" => blocked(grant.Qualifier, args.String("table")),
            "rabbitmq" => blocked(table: args.String("queue")),
            _ => blocked(),
        };
        if (denied) throw Text.Exception("McpAccessDenied");

        bool StorageDenied()
        {
            if (tool == McpToolCatalog.StoragePrepareDelete)
                return args.Strings("keys").Any(key => blocked(key)) || args.Strings("prefixes").Any(prefix => blocked(prefix, descendants: true));
            if (tool is McpToolCatalog.StorageExecuteDelete or McpToolCatalog.StorageDeletePlan)
            {
                string[] keys = cursors.Read<string[]>(args.String("plan_id"), McpToolCatalog.StoragePrepareDelete, grant.Id, "deletion")
                    ?? throw Text.Exception("McpInvalidCursor");
                return keys.Any(key => blocked(key));
            }
            string location = tool is "hermod_storage_list" or McpToolCatalog.StorageStatistics ? args.String("prefix") : args.String("key");
            bool recursive = tool == McpToolCatalog.StorageStatistics
                || tool == McpToolCatalog.StorageStartTransfer && McpStorageTransferPaths.IsDirectoryKey(location);
            return blocked(location, descendants: recursive);
        }
    }
}
