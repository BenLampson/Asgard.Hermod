namespace Asgard.Hermod.Abstractions.Services;

/// <summary>持久化模块负责一致性快照和验证后的事务恢复。</summary>
public interface ILocalWorkspaceStore
{
    /// <summary>完整验证合并候选，失败时尚未上传或修改本机。</summary>
    void Validate(WorkspaceSnapshot snapshot) { }
    /// <summary>导出含连接参数的快照，只允许交给加密服务。</summary>
    Task<WorkspaceSnapshot> ExportAsync(CancellationToken cancellationToken = default);
    /// <summary>完整校验后事务替换实例和标签，并在目标设备重新保护凭据。</summary>
    Task RestoreAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
    /// <summary>本机内容仍与读取时一致才应用云端合并，避免网络期间的编辑丢失。</summary>
    Task RestoreIfUnchangedAsync(WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
        => throw Text.Exception("SyncLocalChanged");
    /// <summary>账户切换与本机替换在同一事务内提交归属。</summary>
    Task SwitchOwnerAsync(string owner, WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
        => throw Text.Exception("SyncLocalChanged");
}
