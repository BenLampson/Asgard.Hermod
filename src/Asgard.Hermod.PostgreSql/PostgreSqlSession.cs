using System.Diagnostics;

namespace Asgard.Hermod.PostgreSql;

/// <summary>串行访问独立连接；SQL 查询和网格写入使用不同会话。</summary>
internal sealed partial class PostgreSqlSession(NpgsqlConnection connection, NpgsqlDataSource source, PostgreSqlSessionOptions options,
    IReadOnlyList<IDisposable> owned, PostgreSqlSshBridge? bridge, IPostgreSqlSessionFactory factory) : IPostgreSqlSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed, _connectionLost, _hasExecutedUserSql, _unknownGridWrite;
    /// <inheritdoc />
    public PostgreSqlSessionOptions Options { get; } = options;
    /// <inheritdoc />
    public string? ServerVersion => IsConnected ? connection.ServerVersion : null;
    /// <inheritdoc />
    public bool IsConnected => !_disposed && connection.State == ConnectionState.Open;
    /// <inheritdoc />
    public event EventHandler? ConnectionLost;
    /// <inheritdoc />
    public PostgreSqlSessionState State { get; private set; } = new(true, false);
    /// <inheritdoc />
    public string CurrentDatabase => connection.Database;
    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken token = default) => SerializedAsync(async () =>
    { await ScalarAsync("SELECT 1", token).ConfigureAwait(false); return true; }, token);
    /// <inheritdoc />
    public Task<IReadOnlyList<PostgreSqlStatement>> SplitAsync(string sql, CancellationToken token = default) => SerializedAsync(() =>
        Task.FromResult(PostgreSqlSql.Split(sql, StandardStrings)), token);
    private bool StandardStrings => !connection.PostgresParameters.TryGetValue("standard_conforming_strings", out string? value) || value == "on";
    private NpgsqlCommand Command(string sql) => new(sql, connection) { CommandTimeout = Options.CommandTimeout };
    private void RequireDatabase(string database)
    {
        if (database.Length > 0 && database != CurrentDatabase) throw Text.Exception("PostgreSqlDatabaseReconnectRequired");
    }
    private async Task<T> SerializedAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_disposed, this); return await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PostgreSqlConflict) { throw; }
        catch (Exception error) when (error is Asgard.Hermod.Abstractions.Localization.ILocalizedError || error.Data["Hermod.ResourceKey"] is string) { throw; }
        catch (Exception error) { throw PostgreSqlFailure.Safe(error); }
        finally
        {
            _gate.Release();
            if (!_disposed && !IsConnected && !_connectionLost) { _connectionLost = true; ConnectionLost?.Invoke(this, EventArgs.Empty); }
        }
    }
    /// <inheritdoc />
    public Task<PostgreSqlExecution> ExplainAsync(string sql, string database, CancellationToken token = default) => ExecuteAsync(PostgreSqlSql.Explain(sql, StandardStrings), database, token);
    /// <inheritdoc />
    public Task<PostgreSqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default) => SerializedAsync(async () =>
    {
        RequireDatabase(database);
        var watch = Stopwatch.StartNew(); var results = new List<PostgreSqlResult>(); var outcomes = new List<PostgreSqlStatementOutcome>();
        PostgreSqlFailure? failure = null;
        bool strings = StandardStrings;
        var statements = PostgreSqlSql.Split(sql, strings);
        if (statements.Count > 0) { _hasExecutedUserSql = true; State = new(null, null); }
        for (int ordinal = 0; ordinal < statements.Count; ordinal++)
        {
            using var command = Command(statements[ordinal].Sql); bool previewStopped = false;
            try
            {
                await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false))
                {
                    int resultIndex = 0;
                    do
                    {
                        if (reader.FieldCount == 0) continue;
                        var result = await ReadResultAsync(reader, ordinal + 1, ++resultIndex, token).ConfigureAwait(false);
                        results.Add(result);
                        if (result.IsTruncated) { previewStopped = true; command.Cancel(); break; }
                    } while (await reader.NextResultAsync(token).ConfigureAwait(false));
                    outcomes.Add(new(ordinal + 1, reader.RecordsAffected, !previewStopped));
                }
                if (previewStopped) { failure = new("PostgreSqlPreviewStopped", "POSTGRESQL_PREVIEW_LIMIT"); break; }
                if (strings != StandardStrings && ordinal + 1 < statements.Count) { failure = new("PostgreSqlModeChanged", "POSTGRESQL_MODE_CHANGED"); break; }
            }
            catch (Exception error)
            {
                if (!outcomes.Any(outcome => outcome.Statement == ordinal + 1)) outcomes.Add(new(ordinal + 1, 0, false));
                failure = previewStopped ? new("PostgreSqlPreviewStopped", "POSTGRESQL_PREVIEW_LIMIT")
                    : token.IsCancellationRequested ? new("PostgreSqlQueryCancelled", "POSTGRESQL_CANCELLED") : PostgreSqlFailure.Safe(error);
                failure = failure.AtStatement(sql, statements[ordinal].Start, statements[ordinal].Length, ordinal + 1);
                break;
            }
        }
        // Npgsql 未公开任意用户 SQL 的事务状态，保守显示未知，不凭 SQL 文本猜测。
        return new PostgreSqlExecution(results, outcomes, failure, watch.Elapsed, State);
    }, token);

    private async Task<PostgreSqlResult> ReadResultAsync(NpgsqlDataReader reader, int statement, int resultIndex, CancellationToken token)
    {
        var columns = reader.GetColumnSchema().Select((column, index) => new PostgreSqlColumn(index, column.ColumnName ?? "", column.DataTypeName ?? "",
            column.DataType ?? typeof(string), column.AllowDBNull ?? true, CurrentDatabase, column.BaseTableName ?? "", column.BaseColumnName ?? "",
            Schema: column.BaseSchemaName ?? "")).ToArray();
        var rows = new List<object?[]>(); long bytes = 0; bool truncated = false;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (rows.Count == 1000) { truncated = true; break; }
            var row = new object?[columns.Length];
            for (int i = 0; i < row.Length; i++)
            {
                var value = await ReadValueAsync(reader, i, 10 * 1024 * 1024 - bytes, token).ConfigureAwait(false);
                if (value.TooLarge) { truncated = true; break; }
                row[i] = value.Value; bytes += value.Bytes;
            }
            if (truncated) break;
            rows.Add(row);
        }
        return new(statement, resultIndex, columns, rows, truncated);
    }
    /// <inheritdoc />
    public Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default) => ReadTableAsync(database, "public", table, offset, 200, "", token);
    /// <inheritdoc />
    public Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, int pageSize, string filter, CancellationToken token = default) => ReadTableAsync(database, "public", table, offset, pageSize, filter, token);
    /// <inheritdoc />
    public Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, CancellationToken token = default) => ReadTableAsync(database, schema, table, offset, 200, "", token);
    /// <inheritdoc />
    public Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, int pageSize, string filter, CancellationToken token = default) => SerializedAsync(async () =>
    {
        RequireDatabase(database);
        var description = await DescribeCoreAsync(database, schema, table, token).ConfigureAwait(false);
        var plan = PostgreSqlTableReadPlan.Create(description, offset, pageSize, filter);
        using var command = Command(plan.Sql);
        foreach (var parameter in plan.Parameters) AddValue(command, parameter.Key, parameter.Value);
        PostgreSqlResult result;
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false))
        { result = await ReadResultAsync(reader, 1, 1, token).ConfigureAwait(false); if (result.IsTruncated) command.Cancel(); }
        return result with
        {
            Columns = description.Columns,
            Rows = result.Rows.Take(pageSize).ToArray(),
            HasMore = !result.IsTruncated && result.Rows.Count > pageSize,
            EditableTable = !result.IsTruncated && description.SupportsEditing ? description : null
        };
    }, token);
    /// <inheritdoc />
    public Task<PostgreSqlSessionState> ReadStateAsync(CancellationToken token = default) => SerializedAsync(() => Task.FromResult(State), token);
    private async Task<object?> ScalarAsync(string sql, CancellationToken token)
    { using var command = Command(sql); return await command.ExecuteScalarAsync(token).ConfigureAwait(false); }
    /// <inheritdoc />
    public Task RollbackAsync(CancellationToken token = default) => SerializedAsync(async () =>
    { using var command = Command("ROLLBACK"); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); State = new(true, false); return true; }, token);
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return; _disposed = true; ConnectionLost = null;
            await connection.DisposeAsync().ConfigureAwait(false); await source.DisposeAsync().ConfigureAwait(false);
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false);
            foreach (var item in owned) item.Dispose();
        }
        finally { _gate.Release(); }
    }
}
