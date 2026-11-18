using System.Globalization;
using System.Text;
using System.Text.Json;
using Asgard.Hermod.MySql;
using Asgard.Hermod.PostgreSql;

namespace Asgard.Hermod.Mcp;

/// <summary>从授权生成限定的查询，不接受外部提供的 SQL、连接参数或表达式。</summary>
internal sealed class McpSqlTools(IMySqlSessionFactory mySql, IPostgreSqlSessionFactory postgres)
{
    internal async Task<object> ListResourcesAsync(string type, string database, int offset, InstanceConnectionOptions connection,
        Func<string, string, bool> allowed, CancellationToken token)
    {
        if (type == "mysql")
        {
            await using var session = await mySql.OpenAsync(connection, token).ConfigureAwait(false);
            var page = await session.ListAsync(new(MySqlObjectKind.Root, ""), offset, token).ConfigureAwait(false);
            return new
            {
                kind = "databases",
                items = page.Items.Where(item => allowed(item.Name, "")).Select(item => item.Name).ToArray(),
                nextOffset = page.HasMore ? (int?)(offset + page.Items.Count) : null
            };
        }
        var options = database.Length == 0 ? connection : new InstanceConnectionOptions
        {
            TypeId = connection.TypeId,
            ProviderId = connection.ProviderId,
            Parameters = new Dictionary<string, string>(connection.Parameters, StringComparer.Ordinal) { ["database"] = database },
        };
        await using var postgresSession = await postgres.OpenAsync(options, token).ConfigureAwait(false);
        var parent = database.Length == 0 ? new PostgreSqlObject(PostgreSqlObjectKind.Root, "")
            : new PostgreSqlObject(PostgreSqlObjectKind.Database, database, database);
        var result = await postgresSession.ListAsync(parent, offset, token).ConfigureAwait(false);
        return new
        {
            kind = database.Length == 0 ? "databases" : "schemas",
            items = result.Items.Where(item => database.Length == 0 ? allowed(item.Name, "") : allowed(database, item.Name)).Select(item => item.Name).ToArray(),
            nextOffset = result.HasMore ? (int?)(offset + result.Items.Count) : null
        };
    }

    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args, CancellationToken token,
        Func<string, bool>? allowed = null)
    {
        string table = args.String("table");
        bool listing = name == "hermod_db_list_tables";
        if (!listing && grant.Table.Length > 0 && grant.Table != table) throw Text.Exception("McpAccessDenied");
        if (listing && grant.Table.Length > 0) return new { tables = new[] { grant.Table }, nextOffset = (int?)null };
        int offset = args.Integer("offset", 0), limit = args.Integer("limit", 10);
        return grant.TypeId == MySqlIds.Type
            ? await MySqlAsync(name, grant.Resource, table, offset, limit, connection, token, allowed).ConfigureAwait(false)
            : await PostgreSqlAsync(name, grant.Resource, grant.Qualifier, table, offset, limit, connection, token, allowed).ConfigureAwait(false);
    }

    private async Task<object> MySqlAsync(string name, string database, string table, int offset, int limit,
        InstanceConnectionOptions connection, CancellationToken token, Func<string, bool>? allowed)
    {
        await using var session = await mySql.OpenAsync(connection, token).ConfigureAwait(false);
        string relation = MySqlSql.Quote(database) + "." + MySqlSql.Quote(table);
        // 字节等值避免 information_schema 的不区分大小写比较把相似库合并。库名不作为 SQL 字符串插入。
        string catalog = $"BINARY TABLE_SCHEMA = X'{Convert.ToHexString(Encoding.UTF8.GetBytes(database))}'";
        string sql = name switch
        {
            "hermod_db_list_tables" => FormattableString.Invariant($"SELECT TABLE_NAME FROM information_schema.TABLES WHERE {catalog} AND TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME LIMIT 101 OFFSET {offset}"),
            "hermod_db_describe_table" => "SHOW COLUMNS FROM " + relation,
            "hermod_db_list_indexes" => "SHOW INDEX FROM " + relation,
            "hermod_db_read_rows" => FormattableString.Invariant($"SELECT * FROM {relation} LIMIT {limit + 1} OFFSET {offset}"),
            _ => throw Text.Exception("McpInvalidArguments"),
        };
        if (name == "hermod_db_read_rows")
        {
            var definition = await session.DescribeAsync(database, table, token).ConfigureAwait(false);
            if (!string.Equals(definition.Engine, "InnoDB", StringComparison.OrdinalIgnoreCase)) throw Text.Exception("McpRowsRequireBaseTable");
            if (definition.Keys.Count > 0)
                sql = FormattableString.Invariant($"SELECT * FROM {relation} ORDER BY {string.Join(", ", definition.Keys.Select(key => MySqlSql.Quote(key.Name)))} LIMIT {limit + 1} OFFSET {offset}");
        }
        var result = (await session.ExecuteReadOnlyAsync(sql, database, token).ConfigureAwait(false)).Results.Single();
        var rows = result.Rows;
        return name switch
        {
            "hermod_db_list_tables" => new { tables = rows.Take(100).Select(row => Cell(result, row, "TABLE_NAME")).Where(name => allowed is null || allowed(name)), nextOffset = NextOffset(offset, 100, rows.Count > 100) },
            "hermod_db_describe_table" => new
            {
                table,
                columns = rows.Take(100).Select(row => new { name = Cell(result, row, "Field"), type = Cell(result, row, "Type"), nullable = Cell(result, row, "Null") == "YES", primaryKey = Cell(result, row, "Key") == "PRI" }),
                truncated = result.IsTruncated || rows.Count > 100,
            },
            "hermod_db_list_indexes" => new
            {
                table,
                indexes = rows.Take(100).Select(row => new { name = Cell(result, row, "Key_name"), column = Cell(result, row, "Column_name"), position = Cell(result, row, "Seq_in_index"), unique = Cell(result, row, "Non_unique") == "0" }),
                truncated = result.IsTruncated || rows.Count > 100,
            },
            _ => Rows(table, result.Columns.Select(column => column.Name).ToArray(), rows, result.IsTruncated, offset, limit),
        };
    }

    private async Task<object> PostgreSqlAsync(string name, string database, string schema, string table, int offset, int limit,
        InstanceConnectionOptions connection, CancellationToken token, Func<string, bool>? allowed)
    {
        // 服务器会截断过长的标识符；在打开会话之前拒绝可能解析到其他对象的范围。
        if (new[] { database, schema, table }.Any(value => Encoding.UTF8.GetByteCount(value) > 63)) throw Text.Exception("McpInvalidArguments");
        var options = new InstanceConnectionOptions
        {
            TypeId = connection.TypeId,
            ProviderId = connection.ProviderId,
            Parameters = new Dictionary<string, string>(connection.Parameters, StringComparer.Ordinal) { ["database"] = database },
        };
        await using var session = await postgres.OpenAsync(options, token).ConfigureAwait(false);
        if (name == "hermod_db_list_tables")
        {
            var page = await session.ListAsync(new(PostgreSqlObjectKind.Tables, "", database, Schema: schema), offset, token).ConfigureAwait(false);
            return new { tables = page.Items.Take(100).Select(item => item.Name).Where(name => allowed is null || allowed(name)), nextOffset = NextOffset(offset, 100, page.HasMore || page.Items.Count > 100) };
        }
        if (name == "hermod_db_list_indexes")
        {
            var page = await session.ListAsync(new(PostgreSqlObjectKind.Indexes, "", database, table, Schema: schema), 0, token).ConfigureAwait(false);
            return new { table, indexes = page.Items.Take(100).Select(item => new { item.Name, definition = McpJson.Clip(item.Detail, 300) }), truncated = page.HasMore || page.Items.Count > 100 };
        }
        var definition = await session.DescribeAsync(database, schema, table, token).ConfigureAwait(false);
        if (name == "hermod_db_describe_table")
            return new { table, columns = definition.Columns.Take(100).Select(column => new { column.Name, type = McpJson.Clip(column.TypeName, 200), column.Nullable, column.PrimaryKey }), truncated = definition.Columns.Count > 100 };
        if (definition.RelationKind != "r") throw Text.Exception("McpRowsRequireBaseTable");
        string relation = PostgreSqlSql.Quote(schema) + "." + PostgreSqlSql.Quote(table);
        string order = definition.Keys.Count == 0 ? "" : " ORDER BY " + string.Join(", ", definition.Keys.Select(key => PostgreSqlSql.Quote(key.Name)));
        // ONLY 排除继承关系隐藏的其他表；执行器在服务端只读事务中运行且不会复用编辑会话。
        string sql = FormattableString.Invariant($"SELECT * FROM ONLY {relation}{order} LIMIT {limit + 1} OFFSET {offset}");
        var result = (await session.ExecuteReadOnlyAsync(sql, database, token).ConfigureAwait(false)).Results.Single();
        return Rows(table, result.Columns.Select(column => column.Name).ToArray(), result.Rows, result.IsTruncated, offset, limit);
    }

    private static string Cell(MySqlResult result, object?[] row, string name)
    {
        var column = result.Columns.FirstOrDefault(value => value.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return column is null ? "" : McpJson.Clip(Convert.ToString(row[column.Ordinal], CultureInfo.InvariantCulture) ?? "", 200);
    }

    private static object Rows(string table, string[] allColumns, IReadOnlyList<object?[]> source, bool truncated, int offset, int limit)
    {
        var columns = allColumns.Take(64).ToArray();
        truncated |= allColumns.Length > columns.Length;
        int remainingBytes = 30 * 1024;
        List<object?[]> rows = [];
        foreach (var row in source.Take(limit))
        {
            var values = new object?[columns.Length];
            for (int i = 0; i < values.Length; i++) values[i] = Scalar(row[i], ref truncated);
            remainingBytes -= JsonSerializer.SerializeToUtf8Bytes(values).Length;
            if (remainingBytes < 0) { truncated = true; break; }
            rows.Add(values);
        }
        return new { table, columns, rows, truncated, nextOffset = NextOffset(offset, rows.Count, source.Count > rows.Count) };
    }

    private static object? Scalar(object? value, ref bool truncated)
    {
        switch (value)
        {
            case null or DBNull: return null;
            case bool or byte or sbyte or short or ushort or int or uint or long or ulong or decimal: return value;
            case float number when float.IsFinite(number): return number;
            case double number when double.IsFinite(number): return number;
            case DateTime time: return time.ToString("O", CultureInfo.InvariantCulture);
            case DateTimeOffset time: return time.ToString("O", CultureInfo.InvariantCulture);
            case DateOnly date: return date.ToString("O", CultureInfo.InvariantCulture);
            case string text:
                truncated |= text.Length > 512;
                return McpJson.Clip(text, 512);
            default: truncated = true; return null;
        }
    }

    private static int? NextOffset(int offset, int count, bool more) => more && count > 0 && offset + count <= 10000 ? offset + count : null;
}
