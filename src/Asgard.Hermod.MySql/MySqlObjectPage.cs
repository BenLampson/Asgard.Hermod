namespace Asgard.Hermod.MySql;

/// <summary>每个分支单独分页，失败不能伪装成空集合。</summary>
public sealed record MySqlObjectPage(IReadOnlyList<MySqlObject> Items, bool HasMore);
