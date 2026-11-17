using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>为每次调用重新核验授权，只返回模型确实能访问的实例和能力。</summary>
internal sealed class McpTools(McpAccessService access, McpSqlTools sql, McpStorageTools storage, McpSshTools ssh, McpRedisTools redis, McpRabbitMqTools queues)
{
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    internal IReadOnlyList<Tool> List(McpSessionAccess session)
    {
        var grants = CurrentGrants(session);
        return McpToolCatalog.All.Values.Where(tool => tool.Types.Length == 0 || grants.Any(pair => tool.Allows(pair.Grant)))
            .Select(tool => tool.ToProtocol()).ToArray();
    }

    internal async Task<CallToolResult> InvokeAsync(McpSessionAccess session, string name, IReadOnlyDictionary<string, JsonElement>? values,
        McpCursorCache cursors, CancellationToken cancellationToken)
    {
        if (!_concurrency.Wait(0, cancellationToken)) return McpJson.Error(Text.Get("McpBusy"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Revoked);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var token = timeout.Token;
            token.ThrowIfCancellationRequested();
            if (!McpToolCatalog.All.TryGetValue(name, out var tool)) throw Text.Exception("McpInvalidArguments");
            var args = McpArguments.Validate(tool.Arguments, values);
            if (tool.Types.Length == 0) return McpJson.Success(new { instances = CurrentGrants(session).Select(Item).ToArray() });
            var grant = session.Policy.Grants.SingleOrDefault(grant => grant.Id == args.String("grant_id") && tool.Allows(grant))
                ?? throw Text.Exception("McpAccessDenied");
            var connection = access.OpenTarget(session, grant);
            token.ThrowIfCancellationRequested();
            object result = grant.TypeId switch
            {
                "mysql" or "postgresql" => await sql.RunAsync(name, grant, connection, args, token).ConfigureAwait(false),
                "storage" => await storage.RunAsync(name, grant, connection, args, cursors, token).ConfigureAwait(false),
                "ssh" => await ssh.RunAsync(name, grant, connection, args, token).ConfigureAwait(false),
                "redis" => await redis.RunAsync(name, grant, connection, args, cursors, token).ConfigureAwait(false),
                "rabbitmq" => await queues.RunAsync(name, grant, connection, args, token).ConfigureAwait(false),
                _ => throw Text.Exception("McpAccessDenied"),
            };
            token.ThrowIfCancellationRequested();
            var response = McpJson.Success(result);
            access.Check(session, grant);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (session.Revoked.IsCancellationRequested) { return McpJson.Error(Text.Get("McpAccessDenied")); }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { return McpJson.Error(Text.Get("McpTimedOut")); }
        catch (Exception error) { return McpJson.Error(Text.Error(error, "McpOperationFailed")); }
        finally { _concurrency.Release(); }
    }

    private (McpGrant Grant, McpTarget Target)[] CurrentGrants(McpSessionAccess session)
    {
        var workspace = access.GetWorkspace(session);
        return (from grant in session.Policy.Grants
                join target in workspace.Targets on grant.InstanceId equals target.Id
                where McpGrantRules.IsSameTarget(target, grant)
                select (grant, target)).ToArray();
    }

    private static object Item((McpGrant Grant, McpTarget Target) pair)
    {
        var (grant, target) = pair;
        object scope = grant.TypeId switch
        {
            "storage" => new { bucket = grant.Resource, prefix = grant.Qualifier },
            "mysql" => new { database = grant.Resource, table = grant.Table.Length > 0 ? grant.Table : null },
            "postgresql" => new { database = grant.Resource, schema = grant.Qualifier, table = grant.Table.Length > 0 ? grant.Table : null },
            "redis" => new { database = int.Parse(grant.Resource, CultureInfo.InvariantCulture), prefix = grant.Qualifier },
            "rabbitmq" => new { vhost = grant.Resource },
            _ => new { service = grant.Resource.Length > 0 ? grant.Resource : null },
        };
        return new { grantId = grant.Id, instanceId = target.Id, target.Name, type = target.TypeId, scope, grant.ShareContent };
    }
}
