namespace Asgard.Hermod.MySql;

/// <summary>编辑仅允许有完整主键的 InnoDB 基表。</summary>
public sealed record MySqlTable(string Database, string Name, IReadOnlyList<MySqlColumn> Columns, string Engine)
{
    /// <summary>由服务器元数据确认的完整主键列。</summary>
    public IReadOnlyList<MySqlColumn> Keys => Columns.Where(column => column.PrimaryKey).ToArray();
    /// <summary>是否满足事务基表、主键和更新权限条件。</summary>
    public bool SupportsEditing => Engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase) && Keys.Count > 0 && Columns.Any(column => column.CanUpdate && !column.Generated);
}
