namespace Asgard.Hermod.UI.Models;

/// <summary>仅允许编辑名称、父级和颜色的标签草稿。</summary>
public sealed record TagEditDraft(string Name, string? ParentId, string Color);
