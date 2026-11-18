using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Mcp;

/// <summary>统一管理设置、配对和授权撤销；模型永远拿不到连接参数。</summary>
internal sealed class McpAccessService(IMcpLocalStore store, string command) : IMcpSettingsService, IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _revocation = new();
    private bool _stopped;

    public Task<McpSettingsSnapshot> ReadAsync(CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        var workspace = store.ReadWorkspace();
        return Snapshot(workspace, Policy(workspace.Owner));
    }, cancellationToken);

    public Task<McpSettingsSnapshot> SetEnabledAsync(string owner, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(owner, (_, policy) => policy.Enabled == enabled ? policy
            : policy with { Enabled = enabled, Secret = enabled ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : "" }, cancellationToken);

    public Task<McpSettingsSnapshot> AddGrantAsync(string owner, McpGrant grant, CancellationToken cancellationToken = default) =>
        UpdateAsync(owner, (workspace, policy) =>
        {
            if (policy.DefaultAllow) throw Text.Exception("McpLegacyGrantUnavailable");
            McpGrantRules.Validate(grant);
            if (!workspace.Targets.Any(target => McpGrantRules.IsSameTarget(target, grant))) throw Text.Exception("McpWorkspaceChanged");
            var candidate = grant with
            {
                Id = "",
                TransferRoot = McpGrantRules.NeedsTransferRoot(grant) ? McpStorageTransferPaths.NormalizeRoot(grant.TransferRoot!) : null,
            };
            if (policy.Grants.Any(value => value with { Id = "" } == candidate)) return policy;
            if (policy.Grants.Count >= McpGrantRules.MaximumGrants) throw Text.Exception("McpGrantLimit");
            return policy with { Grants = Array.AsReadOnly<McpGrant>([.. policy.Grants, candidate with { Id = Guid.NewGuid().ToString("N") }]) };
        }, cancellationToken);

    public Task<McpSettingsSnapshot> RemoveGrantAsync(string owner, string grantId, CancellationToken cancellationToken = default) =>
        UpdateAsync(owner, (_, policy) => policy with { Grants = Array.AsReadOnly(policy.Grants.Where(grant => grant.Id != grantId).ToArray()) }, cancellationToken);

    public Task<McpSettingsSnapshot> SetExclusionsAsync(string owner, IReadOnlyList<McpExclusion> exclusions, CancellationToken cancellationToken = default) =>
        UpdateAsync(owner, (workspace, policy) =>
        {
            ArgumentNullException.ThrowIfNull(exclusions);
            if (exclusions.Count > McpExclusionRules.Maximum) throw Text.Exception("McpExclusionLimit");
            foreach (var exclusion in exclusions)
            {
                McpExclusionRules.Validate(exclusion);
                if (!workspace.Targets.Any(target => target.Id == exclusion.InstanceId && target.TypeId == exclusion.TypeId)
                    && !(policy.Exclusions ?? []).Contains(exclusion)) throw Text.Exception("McpWorkspaceChanged");
            }
            var normalized = exclusions.Select(item => item with { Id = item.Id.Length == 0 ? Guid.NewGuid().ToString("N") : item.Id })
                .DistinctBy(item => (item.InstanceId, item.TypeId, item.Resource, item.Qualifier, item.Table)).ToArray();
            if (policy.DefaultAllow && (policy.Exclusions ?? []).SequenceEqual(normalized)) return policy;
            // 禁用整个实例同时清除持久授权，重新启用时恢复首次访问确认。
            var approvals = policy.InstanceApprovals?.Where(approval =>
                !normalized.Any(item => item.InstanceId == approval.InstanceId && item.Resource.Length == 0)).ToArray();
            return policy with { DefaultAllow = true, Exclusions = Array.AsReadOnly(normalized), InstanceApprovals = approvals };
        }, cancellationToken);

    public Task<McpSettingsSnapshot> SetInstanceApprovalAsync(string owner, McpInstanceApproval approval, bool enabled, CancellationToken cancellationToken = default) =>
        UpdateAsync(owner, (workspace, policy) =>
        {
            var target = workspace.Targets.SingleOrDefault(approval.Matches) ?? throw Text.Exception("McpWorkspaceChanged");
            if (!policy.DefaultAllow || !McpGrantRules.Supports(target.TypeId) || enabled && McpExclusionRules.Blocks(policy, target))
                throw Text.Exception("McpAccessDenied");
            var approvals = policy.InstanceApprovals ?? [];
            if (approvals.Any(item => item.Matches(target)) == enabled) return policy;
            var remaining = approvals.Where(item => item.InstanceId != target.Id && workspace.Targets.Any(item.Matches)).ToList();
            if (enabled) remaining.Add(approval);
            return policy with { InstanceApprovals = remaining.AsReadOnly() };
        }, cancellationToken);

    public Task<string> GetClientConfigurationAsync(string owner, CancellationToken cancellationToken = default) => RunAsync(() =>
    {
        if (store.ReadOwner() != owner) throw Text.Exception("McpWorkspaceChanged");
        if (Policy(owner) is not { Enabled: true } policy) throw Text.Exception("McpAccessDenied");
        return JsonSerializer.Serialize(new
        {
            mcpServers = new { hermod = new { command, env = new Dictionary<string, string> { [McpPipeEndpoint.SecretVariable] = policy.Secret } } },
        }, new JsonSerializerOptions { WriteIndented = true });
    }, cancellationToken);

    /// <summary>默认开放模式不再使用旧白名单；保留加密备份，并防止旧版忽略禁止规则后沿用旧授权。</summary>
    internal void MigrateDefaultPolicy()
    {
        CancellationTokenSource? previous = null;
        lock (_gate)
        {
            if (_stopped) return;
            string owner = store.ReadOwner();
            if (store.ReadPolicy() is not { DefaultAllow: true, Grants.Count: > 0 } policy || policy.Owner != owner) return;
            var legacy = policy.LegacyGrants is null ? policy.Grants.ToArray() : [.. policy.LegacyGrants, .. policy.Grants];
            store.SavePolicy(policy with { Grants = [], LegacyGrants = Array.AsReadOnly(legacy), Generation = Guid.NewGuid().ToString("N") });
            previous = _revocation;
            _revocation = new();
        }
        if (previous is not null) Cancel(previous);
    }

    internal McpSessionAccess? Connect()
    {
        lock (_gate)
        {
            if (_stopped || Policy(store.ReadOwner()) is not { Enabled: true } policy) return null;
            return new(policy, _revocation.Token);
        }
    }

    internal McpWorkspace GetWorkspace(McpSessionAccess access)
    {
        var workspace = store.ReadWorkspace();
        if (workspace.Owner != access.Policy.Owner) throw Text.Exception("McpAccessDenied");
        Check(access);
        return workspace;
    }

    internal InstanceConnectionOptions OpenTarget(McpSessionAccess access, McpGrant grant)
    {
        Check(access);
        return store.OpenTarget(access.Policy.Owner, grant) ?? throw Text.Exception("McpAccessDenied");
    }

    internal void Check(McpSessionAccess access, McpGrant? grant = null)
    {
        lock (_gate)
        {
            if (_stopped || access.Revoked.IsCancellationRequested || store.ReadOwner() != access.Policy.Owner
                || Policy(access.Policy.Owner) is not { Enabled: true } current || current.Generation != access.Policy.Generation
                || (grant is not null && !store.IsCurrent(access.Policy.Owner, grant)))
                throw Text.Exception("McpAccessDenied");
        }
    }

    /// <summary>切换账号或工作区后断开已有客户端，不读取 UI 线程的连接对象。</summary>
    internal void Disconnect()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            if (_stopped) return;
            previous = _revocation;
            _revocation = new();
        }
        Cancel(previous);
    }

    private Task<McpSettingsSnapshot> UpdateAsync(string owner, Func<McpWorkspace, McpLocalPolicy, McpLocalPolicy> update, CancellationToken token) => Task.Run(() =>
    {
        CancellationTokenSource? previous = null;
        McpSettingsSnapshot snapshot;
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_stopped) throw Text.Exception("McpAccessUnavailable");
            var workspace = store.ReadWorkspace();
            if (workspace.Owner != owner) throw Text.Exception("McpWorkspaceChanged");
            var current = Policy(owner);
            var next = update(workspace, current);
            if (!ReferenceEquals(next, current))
            {
                next = next with { Generation = Guid.NewGuid().ToString("N") };
                store.SavePolicy(next);
                previous = _revocation;
                _revocation = new();
            }
            snapshot = Snapshot(workspace, next);
        }
        if (previous is not null) Cancel(previous);
        return snapshot;
    }, token);

    private Task<T> RunAsync<T>(Func<T> operation, CancellationToken token) => Task.Run(() =>
    {
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_stopped) throw Text.Exception("McpAccessUnavailable");
            return operation();
        }
    }, token);

    private McpLocalPolicy Policy(string owner)
    {
        if (store.ReadPolicy() is not { } policy || policy.Owner != owner) return new(owner, "", false, "", []);
        if (policy.Grants is null || policy.Grants.Count > McpGrantRules.MaximumGrants || policy.Exclusions?.Count > McpExclusionRules.Maximum
            || (policy.Enabled && (policy.Generation is not { Length: 32 } || !McpPipeEndpoint.IsSecret(policy.Secret))))
            throw Text.Exception("McpAccessUnavailable");
        return policy;
    }

    private static McpSettingsSnapshot Snapshot(McpWorkspace workspace, McpLocalPolicy policy) =>
        new(workspace with { Targets = Array.AsReadOnly(workspace.Targets.Where(target => McpGrantRules.Supports(target.TypeId)).ToArray()) },
            policy.Enabled, policy.Grants, policy.Exclusions ?? [], policy.DefaultAllow, policy.InstanceApprovals ?? []);

    public void Dispose()
    {
        CancellationTokenSource previous;
        lock (_gate)
        {
            if (_stopped) return;
            _stopped = true;
            previous = _revocation;
        }
        Cancel(previous);
    }

    private static void Cancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (AggregateException) { /* 撤销仍然生效，单个会话的回调不能干扰设置保存。 */ }
        finally { source.Dispose(); }
    }
}
