namespace Asgard.Hermod.Abstractions.Models;

/// <summary>标签管理与选择器共享的快照，路径用于显示，标识用于关联。</summary>
public sealed record ManagedTag(string Id, string Name, string? ParentId, string Color,
    bool IsBuiltIn, string Path, int InstanceCount = 0);
