namespace Asgard.Hermod.UI.Models;

/// <summary>删除前展示完整子树和受影响实例数量。</summary>
public sealed record TagDeletionPreview(string TagId, IReadOnlyList<ManagedTag> Tags, int AffectedInstanceCount);
