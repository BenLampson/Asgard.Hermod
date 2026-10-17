namespace Asgard.Hermod.Abstractions.Models;

/// <summary>标签只传稳定身份和基础字段，路径与计数在目标设备重新生成。</summary>
public sealed record SyncedTag(string Id, string Name, string? ParentId, string Color, bool IsBuiltIn);
