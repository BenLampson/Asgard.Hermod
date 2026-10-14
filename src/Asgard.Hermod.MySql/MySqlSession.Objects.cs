namespace Asgard.Hermod.MySql;

internal sealed partial class MySqlSession
{
    /// <summary>提供模块的公开状态或生命周期入口，协议行为使用稳定标识。</summary>
    public Task<string> ReadDefinitionAsync(MySqlObject item, CancellationToken token = default) => SerializedAsync(async () =>
    {
        var (type, field) = item.Kind switch
        {
            MySqlObjectKind.Table => ("TABLE", "Create Table"),
            MySqlObjectKind.View => ("VIEW", "Create View"),
            MySqlObjectKind.Procedure => ("PROCEDURE", "Create Procedure"),
            MySqlObjectKind.Function => ("FUNCTION", "Create Function"),
            MySqlObjectKind.Event => ("EVENT", "Create Event"),
            MySqlObjectKind.Trigger => ("TRIGGER", "SQL Original Statement"),
            _ => throw Text.Exception("MySqlConfigurationInvalid"),
        };
        using var command = Command($"SHOW CREATE {type} {MySqlSql.Quote(item.Database)}.{MySqlSql.Quote(item.Name)}");
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) throw new MySqlFailure("MySqlRowUnavailable", "MYSQL_OBJECT_MISSING");
        int ordinal = reader.GetOrdinal(field);
        if (reader.IsDBNull(ordinal)) throw new MySqlFailure("MySqlPermissionDenied", "MYSQL_PERMISSION");
        return reader.GetString(ordinal);
    }, token);
    /// <summary>按分支独立读取对象页，失败与空结果不同。</summary>
    public Task<MySqlObjectPage> ListAsync(MySqlObject parent, int offset, CancellationToken token = default) => SerializedAsync(async () =>
    {
        if (offset < 0) throw Text.Exception("MySqlConfigurationInvalid");
        MySqlObject[]? groups = parent.Kind switch
        {
            MySqlObjectKind.Database => Categories(parent, MySqlObjectKind.Tables, MySqlObjectKind.Views, MySqlObjectKind.Procedures, MySqlObjectKind.Functions, MySqlObjectKind.Events),
            MySqlObjectKind.Table => Categories(parent, MySqlObjectKind.Columns, MySqlObjectKind.Indexes, MySqlObjectKind.ForeignKeys, MySqlObjectKind.Triggers),
            MySqlObjectKind.View => Categories(parent, MySqlObjectKind.Columns),
            MySqlObjectKind.Procedure or MySqlObjectKind.Function => Categories(parent, MySqlObjectKind.Parameters),
            _ => null,
        };
        if (groups is not null) return new MySqlObjectPage(groups, false);
        var (sql, kind) = parent.Kind switch
        {
            MySqlObjectKind.Root => ("SELECT SCHEMA_NAME, DEFAULT_CHARACTER_SET_NAME FROM information_schema.SCHEMATA ORDER BY SCHEMA_NAME", MySqlObjectKind.Database),
            MySqlObjectKind.Tables => ("SELECT TABLE_NAME, COALESCE(ENGINE,'') FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_TYPE='BASE TABLE' ORDER BY TABLE_NAME", MySqlObjectKind.Table),
            MySqlObjectKind.Views => ("SELECT TABLE_NAME, '' FROM information_schema.VIEWS WHERE TABLE_SCHEMA=@db ORDER BY TABLE_NAME", MySqlObjectKind.View),
            MySqlObjectKind.Procedures or MySqlObjectKind.Functions => ("SELECT ROUTINE_NAME, DATA_TYPE FROM information_schema.ROUTINES WHERE ROUTINE_SCHEMA=@db AND ROUTINE_TYPE=@type ORDER BY ROUTINE_NAME", parent.Kind == MySqlObjectKind.Procedures ? MySqlObjectKind.Procedure : MySqlObjectKind.Function),
            MySqlObjectKind.Events => ("SELECT EVENT_NAME, STATUS FROM information_schema.EVENTS WHERE EVENT_SCHEMA=@db ORDER BY EVENT_NAME", MySqlObjectKind.Event),
            MySqlObjectKind.Columns => ("SELECT COLUMN_NAME, COLUMN_TYPE FROM information_schema.COLUMNS WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@parent ORDER BY ORDINAL_POSITION", MySqlObjectKind.Column),
            MySqlObjectKind.Indexes => ("SELECT INDEX_NAME, GROUP_CONCAT(COLUMN_NAME ORDER BY SEQ_IN_INDEX SEPARATOR ', ') FROM information_schema.STATISTICS WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@parent GROUP BY INDEX_NAME ORDER BY INDEX_NAME", MySqlObjectKind.Index),
            MySqlObjectKind.ForeignKeys => ("SELECT CONSTRAINT_NAME, GROUP_CONCAT(CONCAT(COLUMN_NAME,' → ',REFERENCED_TABLE_NAME,'.',REFERENCED_COLUMN_NAME) ORDER BY ORDINAL_POSITION SEPARATOR ', ') FROM information_schema.KEY_COLUMN_USAGE WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@parent AND REFERENCED_TABLE_NAME IS NOT NULL GROUP BY CONSTRAINT_NAME ORDER BY CONSTRAINT_NAME", MySqlObjectKind.ForeignKey),
            MySqlObjectKind.Triggers => ("SELECT TRIGGER_NAME, CONCAT(ACTION_TIMING,' ',EVENT_MANIPULATION) FROM information_schema.TRIGGERS WHERE TRIGGER_SCHEMA=@db AND EVENT_OBJECT_TABLE=@parent ORDER BY TRIGGER_NAME", MySqlObjectKind.Trigger),
            MySqlObjectKind.Parameters => ("SELECT COALESCE(PARAMETER_NAME,''), CONCAT(COALESCE(PARAMETER_MODE,'RETURN'),' ',DTD_IDENTIFIER) FROM information_schema.PARAMETERS WHERE SPECIFIC_SCHEMA=@db AND SPECIFIC_NAME=@parent ORDER BY ORDINAL_POSITION", MySqlObjectKind.Parameter),
            _ => ("", MySqlObjectKind.Column),
        };
        if (sql.Length == 0) return new MySqlObjectPage([], false);
        using var command = Command(sql + " LIMIT 201 OFFSET @offset");
        command.Parameters.AddWithValue("@db", parent.Database); command.Parameters.AddWithValue("@parent", parent.ParentName);
        command.Parameters.AddWithValue("@type", parent.Kind == MySqlObjectKind.Procedures ? "PROCEDURE" : "FUNCTION"); command.Parameters.AddWithValue("@offset", offset);
        var items = new List<MySqlObject>(); bool more = false;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (items.Count == 200) { more = true; break; }
            string name = reader.GetString(0), detail = reader.IsDBNull(1) ? "" : reader.GetString(1);
            items.Add(new(kind, name, kind == MySqlObjectKind.Database ? name : parent.Database, parent.ParentName, detail));
        }
        return new MySqlObjectPage(items, more);
    }, token);

    private static MySqlObject[] Categories(MySqlObject parent, params MySqlObjectKind[] kinds) =>
        kinds.Select(kind => new MySqlObject(kind, "", parent.Database, parent.Name)).ToArray();

    /// <summary>读取基表类型、主键和列权限。</summary>
    public Task<MySqlTable> DescribeAsync(string database, string table, CancellationToken token = default) => SerializedAsync(() => DescribeCoreAsync(database, table, token), token);
    private async Task<MySqlTable> DescribeCoreAsync(string database, string table, CancellationToken token)
    {
        string engine;
        using (var command = Command("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=@db AND TABLE_NAME=@table AND TABLE_TYPE='BASE TABLE'"))
        {
            command.Parameters.AddWithValue("@db", database); command.Parameters.AddWithValue("@table", table);
            engine = Convert.ToString(await command.ExecuteScalarAsync(token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        }
        var columns = new List<MySqlColumn>();
        using (var command = Command($"SHOW FULL COLUMNS FROM {MySqlSql.Quote(database)}.{MySqlSql.Quote(table)}"))
        {
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                string name = reader.GetString("Field"), type = reader.GetString("Type"), extra = reader.GetString("Extra");
                columns.Add(new(columns.Count, name, type, typeof(string), reader.GetString("Null") == "YES", database, table, name, reader.GetString("Key") == "PRI",
                    reader.GetString("Privileges").Split(',').Contains("update", StringComparer.OrdinalIgnoreCase),
                    extra.Contains("VIRTUAL GENERATED", StringComparison.OrdinalIgnoreCase) || extra.Contains("STORED GENERATED", StringComparison.OrdinalIgnoreCase)));
            }
        }
        // CLR 类型从真实零行结果获取，不手写 MySQL 类型映射。
        using (var command = Command($"SELECT * FROM {MySqlSql.Quote(database)}.{MySqlSql.Quote(table)} LIMIT 0"))
        {
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            var schema = reader.GetColumnSchema();
            for (int i = 0; i < columns.Count; i++) columns[i] = columns[i] with { ValueType = schema[i].DataType ?? typeof(string), CanUpdate = columns[i].CanUpdate && MySqlValues.SupportsInput(schema[i].DataType ?? typeof(string)) };
        }
        return new(database, table, columns, engine);
    }
}
