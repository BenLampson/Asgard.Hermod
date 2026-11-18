namespace Asgard.Hermod.Mcp;

/// <summary>连接接受时读取设置中的持久授权，弹窗中新增的授权只属于这次连接。</summary>
internal sealed record McpSessionAccess(McpLocalPolicy Policy, CancellationToken Revoked)
{
    private readonly object _approvalGate = new();
    private readonly Dictionary<McpApprovalScope, string> _approved = (Policy.InstanceApprovals ?? []).ToDictionary(
        item => new McpApprovalScope(item.InstanceId, item.TypeId, item.Revision), item => item.DownloadFolder);
    // 并发的同一实例请求在首个确认结束后重新检查，避免重复弹窗。
    internal SemaphoreSlim ApprovalGate { get; } = new(1, 1);

    internal bool HasApproval(McpApprovalScope scope)
    {
        lock (_approvalGate) return !Revoked.IsCancellationRequested && _approved.ContainsKey(scope);
    }

    internal void AddApproval(McpApprovalScope scope, string downloadFolder, CancellationToken requestToken)
    {
        lock (_approvalGate)
        {
            requestToken.ThrowIfCancellationRequested();
            Revoked.ThrowIfCancellationRequested();
            _approved[scope] = downloadFolder;
        }
    }

    internal string DownloadFolder(McpApprovalScope scope)
    {
        lock (_approvalGate) return _approved.GetValueOrDefault(scope, "");
    }
}
