namespace Asgard.Hermod.Mcp;

/// <summary>只登记受限读取能力，保持工具 ID 独立于界面语言。</summary>
internal static class McpToolCatalog
{
    private static readonly McpArgument Grant = new("grant_id", "McpArgGrant", true, 32);
    private static readonly McpArgument Key = new("key", "McpArgKey", true, 1024);
    private static readonly McpArgument Table = new("table", "McpArgTable", true, 128);
    private static readonly McpArgument Offset = new("offset", "McpArgOffset", Minimum: 0, Maximum: 10000);
    private static readonly string[] Sql = ["mysql", "postgresql"];

    internal static IReadOnlyDictionary<string, McpToolDefinition> All { get; } = new McpToolDefinition[]
    {
        new("hermod_list_instances", "McpToolInstances", [], false, []),
        new("hermod_storage_list", "McpToolStorageList", ["storage"], false,
            [Grant, new("prefix", "McpArgPrefix", MaximumLength: 1024), new("cursor", "McpArgCursor", MaximumLength: 64)]),
        new("hermod_storage_stat", "McpToolStorageStat", ["storage"], false, [Grant, Key]),
        new("hermod_storage_read_text", "McpToolStorageRead", ["storage"], true, [Grant, Key]),
        new("hermod_db_list_tables", "McpToolDbList", Sql, false, [Grant, Offset]),
        new("hermod_db_describe_table", "McpToolDbDescribe", Sql, false, [Grant, Table]),
        new("hermod_db_list_indexes", "McpToolDbIndexes", Sql, false, [Grant, Table]),
        new("hermod_db_read_rows", "McpToolDbRows", Sql, true,
            [Grant, Table, Offset, new("limit", "McpArgLimit", Minimum: 1, Maximum: 25)]),
        new("hermod_ssh_diagnose", "McpToolSshDiagnostics", ["ssh"], false,
            [Grant, new("check", "McpArgSshCheck", true, 32, Choices: ["host_summary", "disk_usage", "memory", "service_status"])]),
        new("hermod_ssh_service_logs", "McpToolSshLogs", ["ssh"], true, [Grant]),
        new("hermod_redis_scan_keys", "McpToolRedisScan", ["redis"], false,
            [Grant, new("prefix", "McpArgPrefix", MaximumLength: 1024), new("cursor", "McpArgCursor", MaximumLength: 64)]),
        new("hermod_redis_key_info", "McpToolRedisKey", ["redis"], false, [Grant, Key]),
        new("hermod_rabbitmq_list_queues", "McpToolRabbitQueues", ["rabbitmq"], false,
            [Grant, new("page", "McpArgPage", Minimum: 1, Maximum: 1000)]),
        new("hermod_rabbitmq_queue_info", "McpToolRabbitQueue", ["rabbitmq"], false, [Grant, new("queue", "McpArgQueue", true, 255)]),
    }.ToDictionary(definition => definition.Name, StringComparer.Ordinal);
}
