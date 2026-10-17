namespace Asgard.Hermod.PostgreSql;

internal sealed partial class PostgreSqlSession
{
    /// <inheritdoc />
    public Task<PostgreSqlObjectPage> ListAsync(PostgreSqlObject parent, int offset, CancellationToken token = default) => SerializedAsync(async () =>
    {
        if (offset < 0) throw Text.Exception("PostgreSqlConfigurationInvalid");
        if (parent.Database.Length > 0 && parent.Database != CurrentDatabase)
        {
            // PostgreSQL 不支持跨库限定名，每个外库分支创建独立短连接，不更改查询页签。
            var parameters = Options.Connection.Parameters.ToDictionary(); parameters["database"] = parent.Database;
            await using var other = await factory.OpenAsync(new() { TypeId = PostgreSqlIds.Type, ProviderId = Options.Connection.ProviderId, Parameters = parameters }, token).ConfigureAwait(false);
            return await other.ListAsync(parent, offset, token).ConfigureAwait(false);
        }
        PostgreSqlObject[]? groups = parent.Kind switch
        {
            PostgreSqlObjectKind.Schema => Categories(parent, PostgreSqlObjectKind.Tables, PostgreSqlObjectKind.Views, PostgreSqlObjectKind.Procedures, PostgreSqlObjectKind.Functions),
            PostgreSqlObjectKind.Table => Categories(parent, PostgreSqlObjectKind.Columns, PostgreSqlObjectKind.Indexes, PostgreSqlObjectKind.ForeignKeys, PostgreSqlObjectKind.Triggers),
            PostgreSqlObjectKind.View => Categories(parent, PostgreSqlObjectKind.Columns),
            PostgreSqlObjectKind.Procedure or PostgreSqlObjectKind.Function => Categories(parent, PostgreSqlObjectKind.Parameters),
            _ => null
        };
        if (groups is not null) return new(groups, false);
        var (sql, kind) = parent.Kind switch
        {
            PostgreSqlObjectKind.Root => ("SELECT datname, pg_encoding_to_char(encoding) FROM pg_database WHERE datallowconn AND NOT datistemplate AND has_database_privilege(oid,'CONNECT') ORDER BY datname", PostgreSqlObjectKind.Database),
            PostgreSqlObjectKind.Database => ("SELECT nspname, '' FROM pg_namespace WHERE left(nspname,3)<>'pg_' AND nspname <> 'information_schema' AND has_schema_privilege(oid,'USAGE') ORDER BY nspname", PostgreSqlObjectKind.Schema),
            PostgreSqlObjectKind.Tables => ("SELECT c.relname, c.relkind::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relkind IN ('r','p','f') ORDER BY c.relname", PostgreSqlObjectKind.Table),
            PostgreSqlObjectKind.Views => ("SELECT c.relname, c.relkind::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relkind IN ('v','m') ORDER BY c.relname", PostgreSqlObjectKind.View),
            PostgreSqlObjectKind.Procedures or PostgreSqlObjectKind.Functions => ("SELECT quote_ident(p.proname)||'('||oidvectortypes(p.proargtypes)||')', pg_get_function_result(p.oid) FROM pg_proc p JOIN pg_namespace n ON n.oid=p.pronamespace WHERE n.nspname=@schema AND p.prokind::text=@kind ORDER BY 1", parent.Kind == PostgreSqlObjectKind.Procedures ? PostgreSqlObjectKind.Procedure : PostgreSqlObjectKind.Function),
            PostgreSqlObjectKind.Columns => (PostgreSqlColumnMetadata.Sql, PostgreSqlObjectKind.Column),
            PostgreSqlObjectKind.Indexes => ("SELECT i.relname, pg_get_indexdef(i.oid) FROM pg_index x JOIN pg_class c ON c.oid=x.indrelid JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_class i ON i.oid=x.indexrelid WHERE n.nspname=@schema AND c.relname=@parent ORDER BY i.relname", PostgreSqlObjectKind.Index),
            PostgreSqlObjectKind.ForeignKeys => ("SELECT k.conname, pg_get_constraintdef(k.oid,true) FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relname=@parent AND k.contype='f' ORDER BY k.conname", PostgreSqlObjectKind.ForeignKey),
            PostgreSqlObjectKind.Triggers => ("SELECT t.tgname, pg_get_triggerdef(t.oid,true) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=@schema AND c.relname=@parent AND NOT t.tgisinternal ORDER BY t.tgname", PostgreSqlObjectKind.Trigger),
            PostgreSqlObjectKind.Parameters => ("SELECT COALESCE(p.proargnames[a.ordinality::int],a.ordinality::text), format_type(a.typeid,NULL) FROM pg_proc p CROSS JOIN LATERAL unnest(COALESCE(p.proallargtypes,p.proargtypes::oid[])) WITH ORDINALITY a(typeid,ordinality) WHERE p.oid=to_regprocedure(@routine) ORDER BY a.ordinality", PostgreSqlObjectKind.Parameter),
            _ => ("", PostgreSqlObjectKind.Column)
        };
        if (sql.Length == 0) return new([], false);
        using var command = Command(sql + " LIMIT 201 OFFSET @offset");
        command.Parameters.AddWithValue("schema", parent.Schema); command.Parameters.AddWithValue("parent", parent.ParentName);
        command.Parameters.AddWithValue("kind", parent.Kind == PostgreSqlObjectKind.Procedures ? "p" : "f");
        command.Parameters.AddWithValue("routine", PostgreSqlSql.Quote(parent.Schema) + "." + parent.ParentName);
        command.Parameters.AddWithValue("offset", offset);
        var items = new List<PostgreSqlObject>(); bool more = false;
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (items.Count == 200) { more = true; break; }
            string name = reader.GetString(0), detail = reader.IsDBNull(1) ? "" : reader.GetString(1);
            items.Add(new(kind, name, kind == PostgreSqlObjectKind.Database ? name : parent.Database, parent.ParentName, detail,
                kind == PostgreSqlObjectKind.Schema ? name : parent.Schema)
            { ColumnDefinition = kind == PostgreSqlObjectKind.Column ? PostgreSqlColumnMetadata.Read(reader) : null });
        }
        return new(items, more);
    }, token);
    private static PostgreSqlObject[] Categories(PostgreSqlObject parent, params PostgreSqlObjectKind[] kinds) =>
        kinds.Select(kind => new PostgreSqlObject(kind, "", parent.Database, parent.Name, Schema: parent.Kind == PostgreSqlObjectKind.Schema ? parent.Name : parent.Schema)).ToArray();

    /// <inheritdoc />
    public Task<PostgreSqlTable> DescribeAsync(string database, string table, CancellationToken token = default) => DescribeAsync(database, "public", table, token);
    /// <inheritdoc />
    public Task<PostgreSqlTable> DescribeAsync(string database, string schema, string table, CancellationToken token = default) => SerializedAsync(() => DescribeCoreAsync(database, schema, table, token), token);
    private async Task<PostgreSqlTable> DescribeCoreAsync(string database, string schema, string table, CancellationToken token)
    {
        RequireDatabase(database); var columns = new List<PostgreSqlColumn>(); string relationKind = "";
        const string sql = "SELECT a.attname, format_type(a.atttypid,a.atttypmod), NOT a.attnotnull, c.relkind::text, " +
            "EXISTS(SELECT 1 FROM pg_index i WHERE i.indrelid=c.oid AND i.indisprimary AND a.attnum=ANY(i.indkey)), " +
            "has_column_privilege(c.oid,a.attnum,'UPDATE'), a.attgenerated<>'' OR a.attidentity='a' " +
            "FROM pg_attribute a JOIN pg_class c ON c.oid=a.attrelid JOIN pg_namespace n ON n.oid=c.relnamespace " +
            "WHERE n.nspname=$1 AND c.relname=$2 AND a.attnum>0 AND NOT a.attisdropped ORDER BY a.attnum";
        using (var command = Command(sql))
        {
            command.Parameters.Add(new() { Value = schema }); command.Parameters.Add(new() { Value = table });
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            while (await reader.ReadAsync(token).ConfigureAwait(false))
            {
                relationKind = reader.GetString(3); string name = reader.GetString(0);
                columns.Add(new(columns.Count, name, reader.GetString(1), typeof(string), reader.GetBoolean(2), database, table, name,
                    reader.GetBoolean(4), reader.GetBoolean(5), reader.GetBoolean(6), schema));
            }
        }
        if (columns.Count == 0) throw new PostgreSqlFailure("PostgreSqlRowUnavailable", "POSTGRESQL_OBJECT_MISSING");
        using (var command = Command($"SELECT * FROM {PostgreSqlSql.Quote(schema)}.{PostgreSqlSql.Quote(table)} LIMIT 0"))
        {
            await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
            for (int i = 0; i < columns.Count; i++)
            {
                var type = reader.GetFieldType(i);
                columns[i] = columns[i] with { ValueType = type, CanUpdate = columns[i].CanUpdate && PostgreSqlValues.SupportsInput(type) };
            }
        }
        return new(database, table, columns, relationKind, schema);
    }
    /// <inheritdoc />
    public Task<string> ReadDefinitionAsync(PostgreSqlObject item, CancellationToken token = default) => SerializedAsync(async () =>
    {
        RequireDatabase(item.Database);
        string sql = item.Kind switch
        {
            PostgreSqlObjectKind.View => "SELECT pg_get_viewdef(to_regclass($1),true)",
            PostgreSqlObjectKind.Function or PostgreSqlObjectKind.Procedure => "SELECT pg_get_functiondef(to_regprocedure($1))",
            PostgreSqlObjectKind.Trigger => "SELECT pg_get_triggerdef(t.oid,true) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=$2 AND c.relname=$3 AND t.tgname=$4",
            PostgreSqlObjectKind.Table => "SELECT 'CREATE TABLE '||$1||E' (\\n'||string_agg(part,E',\\n' ORDER BY position)||E'\\n);' FROM (" +
                "SELECT a.attnum AS position, quote_ident(a.attname)||' '||format_type(a.atttypid,a.atttypmod)||" +
                "CASE WHEN a.attidentity='a' THEN ' GENERATED ALWAYS AS IDENTITY' WHEN a.attidentity='d' THEN ' GENERATED BY DEFAULT AS IDENTITY' " +
                "WHEN a.attgenerated<>'' THEN ' GENERATED ALWAYS AS ('||pg_get_expr(d.adbin,d.adrelid)||') '||CASE WHEN a.attgenerated='s' THEN 'STORED' ELSE 'VIRTUAL' END " +
                "WHEN d.adbin IS NOT NULL THEN ' DEFAULT '||pg_get_expr(d.adbin,d.adrelid) ELSE '' END||" +
                "CASE WHEN a.attnotnull THEN ' NOT NULL' ELSE '' END AS part FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum " +
                "WHERE a.attrelid=to_regclass($1) AND a.attnum>0 AND NOT a.attisdropped UNION ALL " +
                "SELECT 100000+row_number() OVER(ORDER BY k.conname), 'CONSTRAINT '||quote_ident(k.conname)||' '||pg_get_constraintdef(k.oid,true) " +
                "FROM pg_constraint k WHERE k.conrelid=to_regclass($1) AND k.contype IN ('p','u','c','f','x')) parts",
            _ => throw Text.Exception("PostgreSqlConfigurationInvalid")
        };
        using var command = Command(sql);
        string qualified = PostgreSqlSql.Quote(item.Schema) + "." + (item.Kind is PostgreSqlObjectKind.Function or PostgreSqlObjectKind.Procedure ? item.Name : PostgreSqlSql.Quote(item.Name));
        command.Parameters.Add(new() { Value = qualified });
        if (item.Kind == PostgreSqlObjectKind.Trigger)
        { command.Parameters.Add(new() { Value = item.Schema }); command.Parameters.Add(new() { Value = item.ParentName }); command.Parameters.Add(new() { Value = item.Name }); }
        var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
        if (result is not string definition) throw new PostgreSqlFailure("PostgreSqlRowUnavailable", "POSTGRESQL_OBJECT_MISSING");
        return item.Kind == PostgreSqlObjectKind.Table ? "-- " + Text.Get("PostgreSqlTableDefinitionPreview") + Environment.NewLine + definition : definition;
    }, token);
}
