namespace Asgard.Hermod.MySql;

/// <summary>只生成有界 SELECT；字段来自真实列目录，所有条件值与分页值均使用参数。</summary>
public sealed record MySqlTableReadPlan(string Sql, IReadOnlyDictionary<string, object?> Parameters)
{
    /// <summary>允许的每页大小不超过现有读取安全上限，并额外探测一行判断是否还有下一页。</summary>
    public static MySqlTableReadPlan Create(MySqlTable table, int offset, int pageSize, string filter)
    {
        if (offset < 0 || pageSize is not (50 or 100 or 200 or 500)) throw Text.Exception("MySqlConfigurationInvalid");
        var condition = new MySqlFilterParser(table.Columns.Select(column => column.Name), filter).Parse();
        var parameters = new Dictionary<string, object?>(condition.Parameters, StringComparer.Ordinal)
        { ["@limit"] = pageSize + 1, ["@offset"] = offset };
        string order = table.Keys.Count == 0 ? "" : " ORDER BY " + string.Join(",", table.Keys.Select(key => MySqlSql.Quote(key.Name)));
        string where = condition.Sql.Length == 0 ? "" : " WHERE " + condition.Sql;
        return new($"SELECT * FROM {MySqlSql.Quote(table.Database)}.{MySqlSql.Quote(table.Name)}{where}{order} LIMIT @limit OFFSET @offset", parameters);
    }
}
