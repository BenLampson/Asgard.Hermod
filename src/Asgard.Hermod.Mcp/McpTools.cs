using System.Globalization;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Asgard.Hermod.Mcp;

/// <summary>为每次调用重新核验授权，只返回模型确实能访问的实例和能力。</summary>
internal sealed class McpTools(McpAccessService access, McpSqlTools sql, McpStorageTools storage, McpSshTools ssh, McpRedisTools redis,
    McpRabbitMqTools queues, McpStorageTransferJobs transfers, IMcpApprovalService? approval = null,
    McpResourceDiscovery? discovery = null, TimeProvider? clock = null)
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(20);
    private readonly SemaphoreSlim _concurrency = new(4, 4);

    internal IReadOnlyList<Tool> List(McpSessionAccess session)
    {
        var grants = CurrentGrants(session);
        return McpToolCatalog.All.Values.Where(tool => (!tool.DefaultOnly || session.Policy.DefaultAllow)
                && (tool.Types.Length == 0 || grants.Any(pair => tool.Allows(pair.Grant))))
            .Select(tool => tool.ToProtocol(session.Policy.DefaultAllow)).ToArray();
    }

    internal async Task<CallToolResult> InvokeAsync(McpSessionAccess session, string name, IReadOnlyDictionary<string, JsonElement>? values,
        McpCursorCache cursors, CancellationToken cancellationToken, Func<ProgressNotificationValue, CancellationToken, Task>? reportProgress = null)
    {
        if (!_concurrency.Wait(0, cancellationToken)) return McpJson.Error(Text.Get("McpBusy"));
        using var deadline = new CancellationTokenSource(OperationTimeout, clock ?? TimeProvider.System);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Revoked, deadline.Token);
        bool waitingForApproval = false;
        try
        {
            var token = lifetime.Token;
            token.ThrowIfCancellationRequested();
            if (!McpToolCatalog.All.TryGetValue(name, out var tool) || tool.DefaultOnly && !session.Policy.DefaultAllow)
                throw Text.Exception("McpInvalidArguments");
            var args = McpArguments.Validate(tool.ArgumentsFor(session.Policy.DefaultAllow), values);
            if (tool.Types.Length == 0)
            {
                var instances = CurrentGrants(session).Select(pair => Item(pair, session)).ToArray();
                return session.Policy.DefaultAllow
                    ? McpJson.Success(new { instances, confirmationHint = Text.Get("McpInstanceConfirmationHint") })
                    : McpJson.Success(new { instances });
            }
            if (name == McpToolCatalog.StorageTransferStatus) return McpJson.Success(transfers.Poll(session, args.String("task_id")));
            var pair = CurrentGrants(session).SingleOrDefault(item => item.Grant.Id == args.String("grant_id") && tool.Allows(item.Grant));
            if (pair.Grant is null) throw Text.Exception("McpAccessDenied");
            var grant = session.Policy.DefaultAllow ? McpDefaultAccess.ForRequest(session, pair.Target, args, name) : pair.Grant;
            if (session.Policy.DefaultAllow)
            {
                // 协议用 / 表示整个 Bucket，内部统一为空前缀，避免与实际对象键混淆。
                if (name == McpToolCatalog.StorageStartTransfer && args.String("key") == "/") args = args.WithString("key", "");
                McpRequestAuthorization.Check(session, pair.Target, grant, name, args, cursors);
                if (name == McpToolCatalog.StorageStartTransfer && args.String("direction") == "upload")
                    McpStorageTransferPaths.ResolveUpload(args.String("local_path"), McpStorageTransferPaths.IsDirectoryKey(args.String("key")));
                var (request, scope) = McpRequestApproval.Create(pair.Target, grant, tool, args, cursors);
                bool newlyApproved = false;
                if (!session.HasApproval(scope))
                {
                    waitingForApproval = true;
                    deadline.CancelAfter(TimeSpan.FromSeconds(30));
                    newlyApproved = await ConfirmInstanceAsync(session, grant, request, scope, reportProgress, token).ConfigureAwait(false);
                    token.ThrowIfCancellationRequested();
                    deadline.CancelAfter(OperationTimeout);
                    waitingForApproval = false;
                }
                if (request.RequiresDownloadFolder)
                {
                    // 本次首次确认尊重用户在弹窗选择的目录；以后显式路径由客户端提供，否则沿用所选目录。
                    string folder = !newlyApproved && request.SuggestedDownloadFolder.Length > 0
                        ? request.SuggestedDownloadFolder : session.DownloadFolder(scope);
                    grant = grant with { TransferRoot = McpStorageTransferPaths.NormalizeRoot(folder) };
                    args = args.WithString("local_path", request.DownloadFileName);
                }
            }
            if (name == McpToolCatalog.StorageStartTransfer) return McpJson.Success(transfers.Start(session, grant, args, token));
            var connection = access.OpenTarget(session, grant);
            token.ThrowIfCancellationRequested();
            if (name == McpToolCatalog.StorageStat && reportProgress is not null)
                await reportProgress(new() { Progress = 1, Message = Text.Get("McpStorageStatQuerying") }, token).ConfigureAwait(false);
            bool allowed(string item) => !McpExclusionRules.Blocks(session.Policy, pair.Target, grant.Resource,
                grant.TypeId is "storage" or "redis" ? item : grant.Qualifier,
                grant.TypeId is "mysql" or "postgresql" or "rabbitmq" ? item : "");
            object result = name == McpToolCatalog.ListResources
                ? await (discovery ?? throw Text.Exception("McpAccessUnavailable")).ListAsync(session, pair.Target, connection, args, token).ConfigureAwait(false)
                : grant.TypeId switch
                {
                    "mysql" or "postgresql" => await sql.RunAsync(name, grant, connection, args, token, session.Policy.DefaultAllow ? allowed : null).ConfigureAwait(false),
                    "storage" => await storage.RunAsync(name, grant, connection, args, cursors, token, session.Policy.DefaultAllow ? allowed : null).ConfigureAwait(false),
                    "ssh" => await ssh.RunAsync(name, grant, connection, args, token).ConfigureAwait(false),
                    "redis" => await redis.RunAsync(name, grant, connection, args, cursors, token, session.Policy.DefaultAllow ? allowed : null).ConfigureAwait(false),
                    "rabbitmq" => await queues.RunAsync(name, grant, connection, args, token, session.Policy.DefaultAllow ? allowed : null).ConfigureAwait(false),
                    _ => throw Text.Exception("McpAccessDenied"),
                };
            token.ThrowIfCancellationRequested();
            var response = McpJson.Success(result);
            access.Check(session, grant);
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (OperationCanceledException) when (session.Revoked.IsCancellationRequested) { return McpJson.Error(Text.Get("McpAccessDenied")); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            string key = waitingForApproval ? "McpApprovalTimedOut" : name == McpToolCatalog.StorageStat ? "McpStorageStatTimedOut" : "McpTimedOut";
            return McpJson.Error(Text.Get(key));
        }
        catch (Exception error) { return McpJson.Error(Text.Error(error, "McpOperationFailed")); }
        finally { _concurrency.Release(); }
    }

    private async Task<bool> ConfirmInstanceAsync(McpSessionAccess session, McpGrant grant, McpApprovalRequest request, McpApprovalScope scope,
        Func<ProgressNotificationValue, CancellationToken, Task>? reportProgress, CancellationToken token)
    {
        if (approval is null) throw Text.Exception("McpApprovalUnavailable");
        if (reportProgress is not null)
            await reportProgress(new() { Progress = 0, Message = Text.Format("McpApprovalWaiting", request.InstanceName) }, token).ConfigureAwait(false);
        await session.ApprovalGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (session.HasApproval(scope)) return false;
            // 即使桌面线程暂时不能处理关闭事件，客户端也能及时结束；过期的决定不进入会话授权。
            var decision = await approval.ConfirmAsync(request, token).WaitAsync(token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            access.Check(session, grant);
            if (!decision.Approved) throw Text.Exception("McpApprovalDenied");
            string folder = decision.DownloadFolder.Length == 0 ? "" : McpStorageTransferPaths.NormalizeRoot(decision.DownloadFolder);
            session.AddApproval(scope, folder, token);
            return true;
        }
        finally { session.ApprovalGate.Release(); }
    }

    private (McpGrant Grant, McpTarget Target)[] CurrentGrants(McpSessionAccess session)
    {
        var workspace = access.GetWorkspace(session);
        if (session.Policy.DefaultAllow)
            return workspace.Targets.Where(target => McpGrantRules.Supports(target.TypeId)
                    && !McpExclusionRules.Blocks(session.Policy, target))
                .Select(target => (McpDefaultAccess.InstanceGrant(session, target), target)).ToArray();
        return (from grant in session.Policy.Grants
                join target in workspace.Targets on grant.InstanceId equals target.Id
                where McpGrantRules.IsSameTarget(target, grant)
                select (grant, target)).ToArray();
    }

    private static object Item((McpGrant Grant, McpTarget Target) pair, McpSessionAccess session)
    {
        var (grant, target) = pair;
        if (session.Policy.DefaultAllow) return new
        {
            grantId = grant.Id,
            instanceId = target.Id,
            target.Name,
            type = target.TypeId,
            scope = new { allResources = true, requiresConfirmation = !session.HasApproval(McpDefaultAccess.Scope(target)) },
        };
        object scope = grant.TypeId switch
        {
            "storage" => new
            {
                bucket = grant.Resource,
                prefix = grant.Qualifier,
                grant.AllowUpload,
                grant.AllowUnrestrictedUpload,
                grant.AllowDownload,
                grant.AllowCreateFolder,
                grant.AllowDelete,
                grant.AllowSignedLinks,
                grant.AllowOverwrite
            },
            "mysql" => new { database = grant.Resource, table = grant.Table.Length > 0 ? grant.Table : null },
            "postgresql" => new { database = grant.Resource, schema = grant.Qualifier, table = grant.Table.Length > 0 ? grant.Table : null },
            "redis" => new { database = int.Parse(grant.Resource, CultureInfo.InvariantCulture), prefix = grant.Qualifier },
            "rabbitmq" => new { vhost = grant.Resource },
            _ => new { service = grant.Resource.Length > 0 ? grant.Resource : null },
        };
        return new { grantId = grant.Id, instanceId = target.Id, target.Name, type = target.TypeId, scope, grant.ShareContent };
    }
}
