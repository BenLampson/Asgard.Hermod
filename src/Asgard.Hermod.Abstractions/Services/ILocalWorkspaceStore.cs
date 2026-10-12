namespace Asgard.Hermod.Abstractions.Services;

/// <summary>持久化模块负责一致性快照和验证后的事务恢复。</summary>
public interface ILocalWorkspaceStore
{
    /// <summary>导出含连接参数的快照，只允许交给加密服务。</summary>
    Task<WorkspaceSnapshot> ExportAsync(CancellationToken cancellationToken = default);
    /// <summary>完整校验后事务替换实例和标签，并在目标设备重新保护凭据。</summary>
    Task RestoreAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default);
}
