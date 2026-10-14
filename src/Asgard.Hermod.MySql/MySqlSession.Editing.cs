namespace Asgard.Hermod.MySql;

internal sealed partial class MySqlSession
{
    public Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(MySqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default) =>
        SerializedAsync(async () => await ReadRowCoreAsync(table, key, null, token).ConfigureAwait(false)
            ?? throw new MySqlFailure("MySqlRowUnavailable", "MYSQL_ROW_MISSING"), token);

    private async Task<IReadOnlyDictionary<string, object?>?> ReadRowCoreAsync(MySqlTable table, IReadOnlyDictionary<string, object?> key,
        MySqlTransaction? transaction, CancellationToken token)
    {
        if (table.Keys.Count == 0 || table.Keys.Any(column => !key.TryGetValue(column.Name, out var value) || value is null))
            throw new MySqlFailure("MySqlReadOnlyResult", "MYSQL_READ_ONLY");
        using var command = Command($"SELECT * FROM {MySqlSql.Quote(table.Database)}.{MySqlSql.Quote(table.Name)} WHERE {KeyPredicate(table)} LIMIT 2" + (transaction is null ? "" : " FOR UPDATE"));
        command.Transaction = transaction; AddKeys(command, table, key);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return null;
        var values = new Dictionary<string, object?>(StringComparer.Ordinal); long bytes = 0;
        for (int i = 0; i < reader.FieldCount; i++)
        {
            object? value = reader.IsDBNull(i) ? null : reader.GetValue(i); bytes += MySqlValues.Bytes(value);
            if (bytes > 10 * 1024 * 1024) throw new MySqlFailure("MySqlRowTooLarge", "MYSQL_READ_ONLY");
            values.Add(reader.GetName(i), value);
        }
        if (await reader.ReadAsync(token).ConfigureAwait(false)) throw new MySqlFailure("MySqlReadOnlyResult", "MYSQL_READ_ONLY");
        return values;
    }

    public Task SaveAsync(MySqlTable table, IReadOnlyList<MySqlRowChange> changes, CancellationToken token = default) => SerializedAsync(async () =>
    {
        if (changes.Count == 0) return true;
        if (changes.Count > 1000) throw Text.Exception("MySqlFieldInvalid");
        var currentTable = await DescribeCoreAsync(table.Database, table.Name, token).ConfigureAwait(false);
        if (!currentTable.SupportsEditing || currentTable.Columns.Count != table.Columns.Count
            || currentTable.Keys.Select(column => column.Name).SequenceEqual(table.Keys.Select(column => column.Name)) == false)
            throw new MySqlFailure("MySqlReadOnlyResult", "MYSQL_READ_ONLY");
        // 网格提交始终在专用的新会话中；禁止把它混进用户 SQL 的未提交事务。
        var state = await ReadStateCoreAsync(token).ConfigureAwait(false);
        if (state.Autocommit != true || state.InTransaction == true) throw new MySqlFailure("MySqlEditTransactionActive", "MYSQL_TRANSACTION");
        foreach (var change in changes)
        {
            if (currentTable.Columns.Any(column => !change.Original.ContainsKey(column.Name)) || change.Original.Count != currentTable.Columns.Count
                || change.Changes.Count == 0 || change.Changes.Any(pair => !currentTable.Columns.Any(column => column.Name == pair.Key && column.CanUpdate && !column.Generated)))
                throw new MySqlFailure("MySqlReadOnlyResult", "MYSQL_READ_ONLY");
        }
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        bool dispatched = false;
        try
        {
            foreach (var change in changes)
            {
                var current = await ReadRowCoreAsync(currentTable, change.Original, transaction, token).ConfigureAwait(false);
                if (current is null || current.Any(pair => !change.Original.TryGetValue(pair.Key, out var original) || !MySqlValues.Equal(original, pair.Value)))
                    throw new MySqlConflict(change, current);
                string assignments = string.Join(",", change.Changes.Keys.Select((name, index) => MySqlSql.Quote(name) + "=@value" + index.ToString(CultureInfo.InvariantCulture)));
                using var command = Command($"UPDATE {MySqlSql.Quote(table.Database)}.{MySqlSql.Quote(table.Name)} SET {assignments} WHERE {KeyPredicate(currentTable)}");
                command.Transaction = transaction; AddKeys(command, currentTable, change.Original);
                int index = 0; foreach (var pair in change.Changes) command.Parameters.AddWithValue("@value" + index++.ToString(CultureInfo.InvariantCulture), pair.Value ?? DBNull.Value);
                token.ThrowIfCancellationRequested(); dispatched = true;
                if (await command.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new MySqlConflict(change, current);
            }
            await transaction.CommitAsync(token).ConfigureAwait(false); return true;
        }
        catch (Exception error)
        {
            bool rolledBack = false;
            try { await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false); rolledBack = true; } catch (Exception) { }
            if (error is MySqlConflict && rolledBack) throw;
            if (!dispatched && token.IsCancellationRequested) throw new OperationCanceledException(token);
            // COMMIT 已发出后即使 ROLLBACK 成功也无法证明此前是否提交，因此不重放。
            throw MySqlFailure.Safe(error, dispatched);
        }
    }, token);

    private static string KeyPredicate(MySqlTable table) => string.Join(" AND ", table.Keys.Select((key, index) => MySqlSql.Quote(key.Name) + " <=> @key" + index.ToString(CultureInfo.InvariantCulture)));
    private static void AddKeys(MySqlCommand command, MySqlTable table, IReadOnlyDictionary<string, object?> values)
    {
        int index = 0;
        foreach (var key in table.Keys) command.Parameters.AddWithValue("@key" + index++.ToString(CultureInfo.InvariantCulture), values[key.Name] ?? DBNull.Value);
    }
}
