namespace Asgard.Hermod.Mcp;

/// <summary>连接接受时冻结策略，用户确认的实例只属于这次连接。</summary>
internal sealed record McpSessionAccess(McpLocalPolicy Policy, CancellationToken Revoked)
{
    private readonly object _approvalGate = new();
    private readonly HashSet<McpApprovalScope> _approved = [];
    private readonly Dictionary<McpApprovalScope, string> _downloadFolders = [];
    // 并发的同一实例请求在首个确认结束后重新检查，避免重复弹窗。
    internal SemaphoreSlim ApprovalGate { get; } = new(1, 1);

    internal bool HasApproval(McpApprovalScope scope)
    {
        lock (_approvalGate) return !Revoked.IsCancellationRequested && _approved.Contains(scope);
    }

    internal void AddApproval(McpApprovalScope scope, string downloadFolder)
    {
        lock (_approvalGate)
        {
            Revoked.ThrowIfCancellationRequested();
            if (downloadFolder.Length > 0) _downloadFolders[scope] = downloadFolder;
            _approved.Add(scope);
        }
    }

    internal string DownloadFolder(McpApprovalScope scope)
    {
        lock (_approvalGate) return _downloadFolders.GetValueOrDefault(scope, "");
    }
}
