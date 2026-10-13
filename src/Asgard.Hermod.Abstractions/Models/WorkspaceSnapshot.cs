namespace Asgard.Hermod.Abstractions.Models;

/// <summary>客户端完整明文工作区，仅在内存合并或设备保护存储中使用；上传前拆分并加密敏感字段。</summary>
public sealed record WorkspaceSnapshot(int SchemaVersion, IReadOnlyList<SyncedInstance> Instances, IReadOnlyList<SyncedTag> Tags);
