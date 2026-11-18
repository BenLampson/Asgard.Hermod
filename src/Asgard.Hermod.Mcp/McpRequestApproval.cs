using System.IO;

namespace Asgard.Hermod.Mcp;

/// <summary>把协议参数冻结成用户可见的实例、资源和位置，不展示凭据或 SDK 错误。</summary>
internal static class McpRequestApproval
{
    internal static (McpApprovalRequest Request, McpApprovalScope Scope) Create(
        McpTarget target, McpGrant grant, McpToolDefinition tool, McpArguments args, McpCursorCache cursors)
    {
        var fields = new List<McpApprovalField>();
        void add(string label, string value) { if (value.Length > 0) fields.Add(new(Text.Get(label), value)); }
        string location = "";
        switch (target.TypeId)
        {
            case "storage":
                add("McpBucket", grant.Resource);
                location = tool.Name is "hermod_storage_list" or McpToolCatalog.StorageStatistics ? args.String("prefix") : args.String("key");
                if (grant.Resource.Length > 0 && tool.Name != McpToolCatalog.StoragePrepareDelete
                    && tool.Name is not (McpToolCatalog.StorageExecuteDelete or McpToolCatalog.StorageDeletePlan))
                    fields.Add(new(Text.Get("McpApprovalLocation"), location.Length == 0 ? Text.Get("McpStorageBucketRoot") : location));
                if (tool.Name == McpToolCatalog.StoragePrepareDelete)
                {
                    var paths = args.Strings("keys").Concat(args.Strings("prefixes")).ToArray();
                    add("McpApprovalLocations", string.Join(Environment.NewLine, paths));
                }
                if (tool.Name is McpToolCatalog.StorageExecuteDelete or McpToolCatalog.StorageDeletePlan)
                {
                    var paths = cursors.Read<string[]>(args.String("plan_id"), McpToolCatalog.StoragePrepareDelete, grant.Id, "deletion")
                        ?? throw Text.Exception("McpInvalidCursor");
                    add("McpApprovalLocations", string.Join(Environment.NewLine, paths));
                }
                break;
            case "mysql":
            case "postgresql":
                add("McpDatabase", grant.Resource.Length > 0 ? grant.Resource : args.String("database"));
                add("McpSchema", grant.Qualifier);
                add("McpTable", args.String("table"));
                break;
            case "redis":
                add("McpRedisDatabase", grant.Resource);
                location = tool.Name == "hermod_redis_scan_keys" ? args.String("prefix") : args.String("key");
                if (grant.Resource.Length > 0) fields.Add(new(Text.Get("McpApprovalLocation"), location.Length == 0 ? Text.Get("McpAnyPrefix") : location));
                break;
            case "rabbitmq":
                add("McpVhost", grant.Resource); add("McpApprovalQueue", args.String("queue"));
                break;
            case "ssh": add("McpServiceUnit", grant.Resource); break;
        }
        bool transfer = tool.Name == McpToolCatalog.StorageStartTransfer;
        bool download = transfer && args.String("direction") == "download";
        if (transfer)
        {
            fields.Add(new(Text.Get("McpApprovalDirection"), Text.Get(download ? "McpApprovalDownload" : "McpApprovalUpload")));
            if (args.String("conflict", "fail") == "overwrite")
                fields.Add(new(Text.Get("McpApprovalOverwrite"), Text.Get("McpApprovalOverwriteYes")));
        }
        string folder = "", file = "";
        if (transfer && !download) add("McpApprovalLocalSource", args.String("local_path"));
        if (download)
        {
            string supplied = args.String("local_path");
            bool directory = McpStorageTransferPaths.IsDirectoryKey(args.String("key"));
            file = directory ? "" : args.String("key")[(args.String("key").LastIndexOf('/') + 1)..];
            if (supplied.Length > 0 && !Path.IsPathFullyQualified(supplied)) throw Text.Exception("McpTransferInvalidDownloadPath");
            if (Path.IsPathFullyQualified(supplied))
            {
                string full = McpStorageTransferPaths.ResolveUpload(supplied, directory);
                folder = directory ? full : Path.GetDirectoryName(full) ?? "";
                if (!directory) file = Path.GetFileName(full);
            }
        }
        var request = new McpApprovalRequest(target.Name, Text.Get(TypeKey(target.TypeId)), Text.Get(ActionKey(tool.Name)),
            fields, download, folder, file, ShowDownloadFolder: target.TypeId == "storage");
        return (request, McpDefaultAccess.Scope(target));
    }

    private static string ActionKey(string name) => name switch
    {
        McpToolCatalog.ListResources => "McpActionListResources",
        "hermod_storage_list" => "McpActionStorageList",
        "hermod_storage_stat" => "McpActionStorageStat",
        McpToolCatalog.StorageStatistics => "McpActionStorageStatistics",
        "hermod_storage_read_text" => "McpActionStorageRead",
        McpToolCatalog.StorageCreateFolder => "McpActionCreateFolder",
        McpToolCatalog.StoragePrepareDelete => "McpActionPrepareDelete",
        McpToolCatalog.StorageDeletePlan => "McpActionViewDeletePlan",
        McpToolCatalog.StorageExecuteDelete => "McpActionDelete",
        McpToolCatalog.StorageSignedLink => "McpActionSignedLink",
        McpToolCatalog.StorageStartTransfer => "McpActionTransfer",
        "hermod_db_list_tables" => "McpActionListTables",
        "hermod_db_describe_table" => "McpActionDescribeTable",
        "hermod_db_list_indexes" => "McpActionListIndexes",
        "hermod_db_read_rows" => "McpActionReadRows",
        "hermod_ssh_diagnose" => "McpActionSshDiagnose",
        "hermod_ssh_service_logs" => "McpActionSshLogs",
        "hermod_redis_scan_keys" => "McpActionRedisScan",
        "hermod_redis_key_info" => "McpActionRedisKey",
        McpToolCatalog.RedisReadValue => "McpActionRedisReadValue",
        "hermod_rabbitmq_list_queues" => "McpActionRabbitList",
        "hermod_rabbitmq_queue_info" => "McpActionRabbitQueue",
        _ => "McpActionAccess",
    };

    private static string TypeKey(string type) => type switch
    {
        "storage" => "ObjectStorage",
        "mysql" => "McpTypeMysql",
        "postgresql" => "McpTypePostgres",
        "redis" => "McpTypeRedis",
        "rabbitmq" => "McpTypeRabbitMq",
        _ => "McpTypeSsh",
    };
}
