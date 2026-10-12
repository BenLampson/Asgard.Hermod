namespace Asgard.Hermod.Abstractions.Models;

/// <summary>跨设备传输的完整工作区；只能在端到端加密边界内传输。</summary>
public sealed record WorkspaceSnapshot(int SchemaVersion, IReadOnlyList<SyncedInstance> Instances, IReadOnlyList<SyncedTag> Tags);
