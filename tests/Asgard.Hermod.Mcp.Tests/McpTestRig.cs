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
    internal MemoryMcpStore Store { get; }
    internal McpAccessService Access { get; }
    internal McpTools Tools { get; }
    internal McpCursorCache Cursors { get; } = new();
    internal McpSessionAccess Session => Access.Connect()!;
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal McpTestRig(McpTarget[] targets, IObjectStorageClientFactory? storage = null, IMySqlSessionFactory? mysql = null,
        IPostgreSqlSessionFactory? postgres = null, ISshConnectionFactory? ssh = null, IRedisSessionFactory? redis = null,
        IRabbitMqSessionFactory? queues = null)
    {
        Store = new(targets);
        Access = new(Store, "Hermod.Mcp.exe");
        Tools = new(Access, new(mysql ?? ProtocolStub<IMySqlSessionFactory>.For(), postgres ?? ProtocolStub<IPostgreSqlSessionFactory>.For()),
            new(storage ?? ProtocolStub<IObjectStorageClientFactory>.For()), new(ssh ?? ProtocolStub<ISshConnectionFactory>.For()),
            new(redis ?? ProtocolStub<IRedisSessionFactory>.For()), new(queues ?? ProtocolStub<IRabbitMqSessionFactory>.For()));
    }

    internal async Task<McpGrant> GrantAsync(McpTarget target, string resource = "", string qualifier = "", string table = "", bool share = false)
    {
        var snapshot = await Access.AddGrantAsync(Store.Owner, new("", target.Id, target.TypeId, target.Revision, resource, qualifier, table, share), Token);
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

    public void Dispose() => Access.Dispose();
}
