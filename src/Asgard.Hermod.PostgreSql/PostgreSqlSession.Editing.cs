namespace Asgard.Hermod.PostgreSql;

internal sealed partial class PostgreSqlSession
{
    /// <summary>按完整主键读取类型化基线。</summary>
    public Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(PostgreSqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default) =>
        SerializedAsync(async () => await ReadRowCoreAsync(table, key, null, token).ConfigureAwait(false)
            ?? throw new PostgreSqlFailure("PostgreSqlRowUnavailable", "POSTGRESQL_ROW_MISSING"), token);

    private async Task<IReadOnlyDictionary<string, object?>?> ReadRowCoreAsync(PostgreSqlTable table, IReadOnlyDictionary<string, object?> key,
        NpgsqlTransaction? transaction, CancellationToken token)
    {
        RequireDatabase(table.Database);
        if (!table.SupportsEditing || table.Keys.Any(column => !key.TryGetValue(column.Name, out var value) || value is null))
            throw new PostgreSqlFailure("PostgreSqlReadOnlyResult", "POSTGRESQL_READ_ONLY");
        using var command = Command($"SELECT * FROM {PostgreSqlSql.Quote(table.Schema)}.{PostgreSqlSql.Quote(table.Name)} WHERE {KeyPredicate(table)} LIMIT 2" + (transaction is null ? "" : " FOR UPDATE"));
        command.Transaction = transaction; AddKeys(command, table, key);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal); long bytes = 0;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var value = await ReadValueAsync(reader, i, 10 * 1024 * 1024 - bytes, token).ConfigureAwait(false);
            if (value.TooLarge) { command.Cancel(); throw new PostgreSqlFailure("PostgreSqlRowTooLarge", "POSTGRESQL_READ_ONLY"); }
            bytes += value.Bytes; values.Add(reader.GetName(i), value.Value);
        }
        if (await reader.ReadAsync(token).ConfigureAwait(false)) throw new PostgreSqlFailure("PostgreSqlReadOnlyResult", "POSTGRESQL_READ_ONLY");
        return values;
    }

    /// <summary>在专用事务内锁定并比较原行，再提交更改；未知结果不重放。</summary>
    public Task SaveAsync(PostgreSqlTable table, IReadOnlyList<PostgreSqlRowChange> changes, CancellationToken token = default) => SerializedAsync(async () =>
    {
        if (changes.Count == 0) return true;
        if (_unknownGridWrite) throw new PostgreSqlFailure("PostgreSqlSaveOutcomeUnknown", "POSTGRESQL_WRITE_UNKNOWN");
        if (_hasExecutedUserSql) throw new PostgreSqlFailure("PostgreSqlDedicatedWriterRequired", "POSTGRESQL_WRITER_REQUIRED");
        if (changes.Count > 1000) throw Text.Exception("PostgreSqlFieldInvalid");
        var currentTable = await DescribeCoreAsync(table.Database, table.Schema, table.Name, token).ConfigureAwait(false);
        if (!currentTable.SupportsEditing || currentTable.Columns.Count != table.Columns.Count
            || currentTable.Keys.Select(column => column.Name).SequenceEqual(table.Keys.Select(column => column.Name)) == false)
            throw new PostgreSqlFailure("PostgreSqlReadOnlyResult", "POSTGRESQL_READ_ONLY");
        // 网格提交始终在专用的新会话中；禁止把它混进用户 SQL 的未提交事务。
        var state = State;
        if (state.Autocommit != true || state.InTransaction == true) throw new PostgreSqlFailure("PostgreSqlEditTransactionActive", "POSTGRESQL_TRANSACTION");
        foreach (var change in changes)
        {
            if (currentTable.Columns.Any(column => !change.Original.ContainsKey(column.Name)) || change.Original.Count != currentTable.Columns.Count
                || change.Changes.Count == 0 || change.Changes.Any(pair => !currentTable.Columns.Any(column => column.Name == pair.Key && column.CanUpdate && !column.Generated)))
                throw new PostgreSqlFailure("PostgreSqlReadOnlyResult", "POSTGRESQL_READ_ONLY");
        }
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        bool dispatched = false;
        try
        {
            foreach (var change in changes)
            {
                var current = await ReadRowCoreAsync(currentTable, change.Original, transaction, token).ConfigureAwait(false);
                if (current is null || current.Any(pair => !change.Original.TryGetValue(pair.Key, out var original) || !PostgreSqlValues.Equal(original, pair.Value)))
                    throw new PostgreSqlConflict(change, current);
                string assignments = string.Join(",", change.Changes.Keys.Select((name, index) => PostgreSqlSql.Quote(name) + "=@value" + index.ToString(CultureInfo.InvariantCulture)));
                using var command = Command($"UPDATE {PostgreSqlSql.Quote(table.Schema)}.{PostgreSqlSql.Quote(table.Name)} SET {assignments} WHERE {KeyPredicate(currentTable)}");
                command.Transaction = transaction; AddKeys(command, currentTable, change.Original);
                int index = 0; foreach (var pair in change.Changes) AddValue(command, "@value" + index++.ToString(CultureInfo.InvariantCulture), pair.Value);
                token.ThrowIfCancellationRequested(); dispatched = true;
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new PostgreSqlConflict(change, current);
            }
            await transaction.CommitAsync(token).ConfigureAwait(false); return true;
        }
        catch (Exception error)
        {
            bool rolledBack = false;
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); rolledBack = true; } catch (Exception) { }
            if (error is PostgreSqlConflict && rolledBack) throw;
            if (!dispatched && token.IsCancellationRequested) throw new OperationCanceledException(token);
            // COMMIT 已发出后即使 ROLLBACK 成功也无法证明此前是否提交，因此不重放。
            var failure = PostgreSqlFailure.Safe(error, dispatched);
            if (failure.Code == "POSTGRESQL_WRITE_UNKNOWN") _unknownGridWrite = true;
            throw failure;
        }
    }, token);

    private static string KeyPredicate(PostgreSqlTable table) => string.Join(" AND ", table.Keys.Select((key, index) => PostgreSqlSql.Quote(key.Name) + " IS NOT DISTINCT FROM @key" + index.ToString(CultureInfo.InvariantCulture)));
    private static void AddKeys(NpgsqlCommand command, PostgreSqlTable table, IReadOnlyDictionary<string, object?> values)
    {
        int index = 0;
        foreach (var key in table.Keys) AddValue(command, "@key" + index++.ToString(CultureInfo.InvariantCulture), values[key.Name]);
    }
    // 字符串以 unknown 交给目标列推导，避免把 jsonb、枚举或 uuid 错发成 text。
    private static void AddValue(NpgsqlCommand command, string name, object? value)
    {
        var parameter = new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value };
        if (value is null or string) parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown;
        command.Parameters.Add(parameter);
    }
}
