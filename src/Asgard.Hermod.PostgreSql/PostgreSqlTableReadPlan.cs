namespace Asgard.Hermod.PostgreSql;

/// <summary>只生成有界 SELECT；字段来自真实列目录，所有条件值与分页值均使用参数。</summary>
public sealed record PostgreSqlTableReadPlan(string Sql, IReadOnlyDictionary<string, object?> Parameters)
{
    /// <summary>允许的每页大小不超过现有读取安全上限，并额外探测一行判断是否还有下一页。</summary>
    public static PostgreSqlTableReadPlan Create(PostgreSqlTable table, int offset, int pageSize, string filter)
    {
        if (offset < 0 || pageSize is not (50 or 100 or 200 or 500)) throw Text.Exception("PostgreSqlConfigurationInvalid");
        var condition = new PostgreSqlFilterParser(table.Columns.Select(column => column.Name), filter).Parse();
        var parameters = new Dictionary<string, object?>(condition.Parameters, StringComparer.Ordinal)
        { ["@limit"] = pageSize + 1, ["@offset"] = offset };
        string order = table.Keys.Count == 0 ? "" : " ORDER BY " + string.Join(",", table.Keys.Select(key => PostgreSqlSql.Quote(key.Name)));
        string where = condition.Sql.Length == 0 ? "" : " WHERE " + condition.Sql;
        return new($"SELECT * FROM {PostgreSqlSql.Quote(table.Schema)}.{PostgreSqlSql.Quote(table.Name)}{where}{order} LIMIT @limit OFFSET @offset", parameters);
    }
}
