using System.Text.Json;
using Asgard.Hermod.MySql;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;
using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.RabbitMq;
using Asgard.Hermod.Redis;
using Asgard.Hermod.Ssh;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp.Tests;

internal sealed class McpTestRig : IDisposable
{
    private readonly McpStorageTransferJobs _transfers;
    internal MemoryMcpStore Store { get; }
    internal McpAccessService Access { get; }
    internal McpTools Tools { get; }
    internal McpCursorCache Cursors { get; } = new();
    internal McpSessionAccess Session => Access.Connect()!;
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal McpTestRig(McpTarget[] targets, IObjectStorageClientFactory? storage = null, IMySqlSessionFactory? mysql = null,
        IPostgreSqlSessionFactory? postgres = null, ISshConnectionFactory? ssh = null, IRedisSessionFactory? redis = null,
        IRabbitMqSessionFactory? queues = null, TimeProvider? clock = null, bool defaultAccess = false, IMcpApprovalService? approval = null)
    {
        Store = new(targets) { Policy = new("", "", false, "", [], DefaultAllow: defaultAccess) };
        Access = new(Store, "Hermod.Mcp.exe");
        var factory = storage ?? ProtocolStub<IObjectStorageClientFactory>.For();
        var storageClients = new McpStorageClients(factory);
        var sql = new McpSqlTools(mysql ?? ProtocolStub<IMySqlSessionFactory>.For(), postgres ?? ProtocolStub<IPostgreSqlSessionFactory>.For());
        var sshTools = new McpSshTools(ssh ?? ProtocolStub<ISshConnectionFactory>.For());
        var redisTools = new McpRedisTools(redis ?? ProtocolStub<IRedisSessionFactory>.For());
        var queueTools = new McpRabbitMqTools(queues ?? ProtocolStub<IRabbitMqSessionFactory>.For());
        _transfers = new(Access, new(Access, storageClients), clock ?? TimeProvider.System);
        Tools = new(Access, sql, new(storageClients), sshTools, redisTools, queueTools, _transfers, approval,
            new(new McpStorageScopeBrowser(Store, factory), sql, redisTools, queueTools, sshTools), clock);
    }

    internal async Task<McpGrant> GrantAsync(McpTarget target, string resource = "", string qualifier = "", string table = "", bool share = false,
        string? transferRoot = null, bool upload = false, bool download = false, bool createFolder = false, bool delete = false, bool signedLinks = false,
        bool overwrite = false, bool unrestrictedUpload = false)
    {
        var snapshot = await Access.AddGrantAsync(Store.Owner, new("", target.Id, target.TypeId, target.Revision, resource, qualifier, table, share,
            transferRoot, upload, download, createFolder, delete, signedLinks, overwrite, unrestrictedUpload), Token);
        return snapshot.Grants[^1];
    }

    internal Task<CallToolResult> CallAsync(string name, McpGrant grant, params (string Key, object Value)[] args) =>
        Tools.InvokeAsync(Session, name, Inputs(grant, args), Cursors, Token);

    internal static Dictionary<string, JsonElement> Inputs(McpGrant grant, params (string Key, object Value)[] args)
    {
        var values = args.ToDictionary(pair => pair.Key, pair => JsonSerializer.SerializeToElement(pair.Value));
        values.Add("grant_id", JsonSerializer.SerializeToElement(grant.Id));
        return values;
    }

    public void Dispose()
    {
        _transfers.Dispose();
        Access.Dispose();
    }
}
