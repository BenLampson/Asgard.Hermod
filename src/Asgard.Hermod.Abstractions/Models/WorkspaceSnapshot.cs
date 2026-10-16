namespace Asgard.Hermod.Abstractions.Models;

/// <summary>客户端完整明文工作区，仅在内存合并或设备保护存储中使用；上传前拆分并加密敏感字段。</summary>
public sealed record WorkspaceSnapshot(int SchemaVersion, IReadOnlyList<SyncedInstance> Instances, IReadOnlyList<SyncedTag> Tags)
{
    /// <summary>模型配置只在内存或设备保护的快照中使用，网络编码必须另行加密。</summary>
    public AgentModelSet AgentModels { get; init; } = AgentModelSet.Empty;
}
