namespace Asgard.Hermod.Mcp;

/// <summary>登记受限能力，保持工具 ID 独立于界面语言。</summary>
internal static class McpToolCatalog
{
    internal const string StorageStartTransfer = "hermod_storage_start_transfer";
    internal const string StorageTransferStatus = "hermod_storage_transfer_status";
    internal const string StorageCreateFolder = "hermod_storage_create_folder";
    internal const string StoragePrepareDelete = "hermod_storage_prepare_delete";
    internal const string StorageDeletePlan = "hermod_storage_delete_plan";
    internal const string StorageExecuteDelete = "hermod_storage_execute_delete";
    internal const string StorageSignedLink = "hermod_storage_create_temporary_link";
    internal const string StorageStatistics = "hermod_storage_folder_statistics";
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
        new(StorageStatistics, "McpToolStorageStatistics", ["storage"], false,
            [Grant, new("prefix", "McpArgStatisticsPrefix", MaximumLength: 1024)]),
        new("hermod_storage_read_text", "McpToolStorageRead", ["storage"], true, [Grant, Key]),
        new(StorageCreateFolder, "McpToolStorageCreateFolder", ["storage"], false,
            [Grant, new("key", "McpArgFolderKey", true, 1024)], ReadOnly: false, RequiresCreateFolder: true),
        new(StoragePrepareDelete, "McpToolStoragePrepareDelete", ["storage"], false,
            [Grant, new("keys", "McpArgDeleteKeys", MaximumLength: 1024, MaximumItems: 1000),
                new("prefixes", "McpArgDeletePrefixes", MaximumLength: 1024, MaximumItems: 100)], RequiresDelete: true),
        new(StorageDeletePlan, "McpToolStorageDeletePlan", ["storage"], false,
            [Grant, new("plan_id", "McpArgDeletePlan", true, 32), new("offset", "McpArgDeleteOffset", Minimum: 0, Maximum: 1000)],
            LocalOnly: true, RequiresDelete: true),
        new(StorageExecuteDelete, "McpToolStorageExecuteDelete", ["storage"], false,
            [Grant, new("plan_id", "McpArgDeletePlan", true, 32)], ReadOnly: false, RequiresDelete: true, Destructive: true),
        new(StorageSignedLink, "McpToolStorageSignedLink", ["storage"], false,
            [Grant, Key, new("lifetime_minutes", "McpArgLinkLifetime", true, Minimum: 15, Maximum: 60, IntegerChoices: [15, 60])], RequiresSignedLinks: true),
        new(StorageStartTransfer, "McpToolStorageStartTransfer", ["storage"], false,
            [Grant, new("direction", "McpArgTransferDirection", true, 8, Choices: ["upload", "download"]),
                new("key", "McpArgTransferKey", true, 1024), new("local_path", "McpArgTransferLocalPath", MaximumLength: 1024),
                new("conflict", "McpArgTransferConflict", MaximumLength: 9, Choices: ["fail", "overwrite"])],
            RequiresTransfer: true, ReadOnly: false, Destructive: true),
        new(StorageTransferStatus, "McpToolStorageTransferStatus", ["storage"], false,
            [new("task_id", "McpArgTransferTaskId", true, 32)], RequiresTransfer: true, LocalOnly: true),
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
