namespace Asgard.Hermod.PostgreSql;

/// <summary>只允许有完整主键和列更新权限的 PostgreSQL 普通基表编辑。</summary>
public sealed record PostgreSqlTable(string Database, string Name, IReadOnlyList<PostgreSqlColumn> Columns, string RelationKind, string Schema = "public")
{
    /// <summary>由服务器目录确认的完整主键。</summary>
    public IReadOnlyList<PostgreSqlColumn> Keys => Columns.Where(column => column.PrimaryKey).ToArray();
    /// <summary>分区表和外部表不自动视为可安全编辑的普通基表。</summary>
    public bool SupportsEditing => RelationKind == "r" && Keys.Count > 0 && Columns.All(column => PostgreSqlValues.SupportsInput(column.ValueType)) && Columns.Any(column => column.CanUpdate && !column.Generated);
}
