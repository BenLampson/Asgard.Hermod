namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>仓储内部标签字段，不把名称或路径作为持久化关联键。</summary>
internal sealed record StoredTag(string Id, string Name, string? ParentId, string Color, bool IsBuiltIn);
