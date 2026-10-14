using System.Diagnostics;

namespace Asgard.Hermod.MySql;

/// <summary>每个会话只执行一个请求，连接失败后不透明重连。</summary>
internal sealed partial class MySqlSession(MySqlConnection connection, MySqlSessionOptions options,
    IReadOnlyList<IDisposable> owned, MySqlSshBridge? bridge) : IMySqlSession
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;
    private bool _connectionLost;
    private bool _hasExecutedUserSql;
    private bool _unknownGridWrite;
    /// <summary>本会话已验证的连接选项。</summary>
    public MySqlSessionOptions Options { get; } = options;
    /// <summary>当前物理连接是否可用；失败后不自动重连。</summary>
    public bool IsConnected => !_disposed && connection.State == ConnectionState.Open;
    /// <inheritdoc />
    public event EventHandler? ConnectionLost;
    /// <inheritdoc />
    public Task<bool> PingAsync(CancellationToken token = default) => SerializedAsync(() => connection.PingAsync(token), token);
    /// <summary>服务器报告的自动提交和事务状态；无法读取时保留未知。</summary>
    public MySqlSessionState State { get; private set; } = new(null, null);
    /// <summary>提供模块的公开状态或生命周期入口，协议行为使用稳定标识。</summary>
    public string CurrentDatabase => connection.Database;
    /// <summary>提供模块的公开状态或生命周期入口，协议行为使用稳定标识。</summary>
    public Task<IReadOnlyList<MySqlStatement>> SplitAsync(string sql, CancellationToken token = default) => SerializedAsync(async () =>
    {
        string mode = Convert.ToString(await ScalarAsync("SELECT @@sql_mode", token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        return MySqlSql.Split(sql, mode.Split(',').Contains("NO_BACKSLASH_ESCAPES", StringComparer.OrdinalIgnoreCase));
    }, token);

    private MySqlCommand Command(string sql) => new(sql, connection) { CommandTimeout = Options.CommandTimeout };

    /// <summary>元数据请求使用同一锁，避免与该页签 SQL 交错。</summary>
    private async Task<T> SerializedAsync<T>(Func<Task<T>> operation, CancellationToken token)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { ObjectDisposedException.ThrowIf(_disposed, this); return await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (MySqlConflict) { throw; }
        catch (Exception error) when (error is Asgard.Hermod.Abstractions.Localization.ILocalizedError) { throw; }
        catch (Exception error) { throw MySqlFailure.Safe(error); }
        finally
        {
            _gate.Release();
            if (!_disposed && !IsConnected && !_connectionLost) { _connectionLost = true; ConnectionLost?.Invoke(this, EventArgs.Empty); }
        }
    }

    /// <summary>串行执行 SQL，保留已完成的结果和安全失败状态。</summary>
    public Task<MySqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default) => SerializedAsync(async () =>
    {
        var watch = Stopwatch.StartNew(); var results = new List<MySqlResult>(); var outcomes = new List<MySqlStatementOutcome>();
        MySqlFailure? failure = null;
        if (database.Length > 0 && connection.Database != database) await connection.ChangeDatabaseAsync(database, token).ConfigureAwait(false);
        string mode = Convert.ToString(await ScalarAsync("SELECT @@sql_mode", token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        var statements = MySqlSql.Split(sql, mode.Split(',').Contains("NO_BACKSLASH_ESCAPES", StringComparer.OrdinalIgnoreCase));
        if (statements.Count > 0) _hasExecutedUserSql = true;
        for (int ordinal = 0; ordinal < statements.Count; ordinal++)
        {
            using var command = Command(statements[ordinal].Sql);
            bool previewStopped = false;
            try
            {
                await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
                {
                    int resultIndex = 0;
                    do
                    {
                        if (reader.FieldCount == 0) continue;
                        var result = await ReadResultAsync(reader, ordinal + 1, ++resultIndex, token).ConfigureAwait(false);
                        results.Add(result);
                        if (result.IsTruncated)
                        {
                            // 及时停止服务器继续传送大结果；不暗改 SQL 或偷偷追加 LIMIT。
                            previewStopped = true; command.Cancel(); break;
                        }
                    } while (await reader.NextResultAsync(token).ConfigureAwait(false));
                    outcomes.Add(new(ordinal + 1, reader.RecordsAffected, !previewStopped));
                }
                if (previewStopped) { failure = new("MySqlPreviewStopped", "MYSQL_PREVIEW_LIMIT"); break; }
                // SQL_MODE 改变后的批次需要重新解析，避免把后续文本按旧转义规则执行。
                string nextMode = Convert.ToString(await ScalarAsync("SELECT @@sql_mode", token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
                if (nextMode != mode && ordinal + 1 < statements.Count) { failure = new("MySqlModeChanged", "MYSQL_MODE_CHANGED"); break; }
            }
            catch (Exception error)
            {
                if (!outcomes.Any(outcome => outcome.Statement == ordinal + 1)) outcomes.Add(new(ordinal + 1, 0, false));
                failure = previewStopped ? new("MySqlPreviewStopped", "MYSQL_PREVIEW_LIMIT")
                    : token.IsCancellationRequested ? new("MySqlQueryCancelled", "MYSQL_CANCELLED") : MySqlFailure.Safe(error);
                break;
            }
        }
        // 元数据只能在 reader 释放后读取；权限不足仍保留查询结果。
        for (int i = 0; i < results.Count; i++)
        {
            var result = results[i];
            if (result.IsTruncated || !MySqlSql.IsSimpleProjection(statements[result.Statement - 1].Sql)) continue;
            var source = MySqlSql.ProjectionSource(statements[result.Statement - 1].Sql);
            if (source is null || result.Columns.Count == 0 || source.Value.Table != result.Columns[0].Table
                || source.Value.Database.Length > 0 && source.Value.Database != result.Columns[0].Database) continue;
            try { results[i] = await AttachEditingAsync(result, token).ConfigureAwait(false); }
            catch (Exception) { /* 无法证明编辑资格时保持只读。 */ }
        }
        try { await ReadStateCoreAsync(token).ConfigureAwait(false); } catch (Exception) { State = new(null, null); }
        return new MySqlExecution(results, outcomes, failure, watch.Elapsed, State);
    }, token);

    private static async Task<MySqlResult> ReadResultAsync(MySqlDataReader reader, int statement, int resultIndex, CancellationToken token)
    {
        var columns = reader.GetColumnSchema().Select((column, index) => new MySqlColumn(index, column.ColumnName ?? "", column.DataTypeName ?? "",
            column.DataType ?? typeof(string), column.AllowDBNull ?? true, column.BaseSchemaName ?? "", column.BaseTableName ?? "", column.BaseColumnName ?? "", column.IsKey ?? false)).ToArray();
        var rows = new List<object?[]>(); long bytes = 0; bool truncated = false;
        while (await reader.ReadAsync(token).ConfigureAwait(false))
        {
            if (rows.Count == 1000) { truncated = true; break; }
            var row = new object?[columns.Length];
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                bytes += MySqlValues.Bytes(row[i]);
            }
            if (bytes > 10 * 1024 * 1024) { truncated = true; break; }
            rows.Add(row);
        }
        return new(statement, resultIndex, columns, rows, truncated);
    }

    private async Task<MySqlResult> AttachEditingAsync(MySqlResult result, CancellationToken token)
    {
        if (result.Columns.Count == 0) return result;
        var first = result.Columns[0];
        if (first.Database.Length == 0 || first.Table.Length == 0 || result.Columns.Any(column => column.Database != first.Database || column.Table != first.Table || column.BaseName.Length == 0)
            || result.Columns.Select(column => column.BaseName).Distinct(StringComparer.Ordinal).Count() != result.Columns.Count) return result;
        var table = await DescribeCoreAsync(first.Database, first.Table, token).ConfigureAwait(false);
        if (!table.SupportsEditing || table.Keys.Any(key => !result.Columns.Any(column => column.BaseName == key.Name))) return result;
        var columns = result.Columns.Select(column =>
        {
            var real = table.Columns.FirstOrDefault(item => item.Name == column.BaseName);
            return real is null ? column : column with { TypeName = real.TypeName, CanUpdate = real.CanUpdate && !real.Generated, Generated = real.Generated, PrimaryKey = real.PrimaryKey };
        }).ToArray();
        return result with { Columns = columns, EditableTable = table };
    }

    /// <summary>读取一页完整表行，不冒充全表结果。</summary>
    public Task<MySqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default) => ReadTableAsync(database, table, offset, 200, "", token);

    /// <inheritdoc />
    public Task<MySqlResult> ReadTableAsync(string database, string table, int offset, int pageSize, string filter, CancellationToken token = default) => SerializedAsync(async () =>
    {
        if (offset < 0) throw Text.Exception("MySqlConfigurationInvalid");
        var description = await DescribeCoreAsync(database, table, token).ConfigureAwait(false);
        var plan = MySqlTableReadPlan.Create(description, offset, pageSize, filter);
        using var command = Command(plan.Sql);
        foreach (var parameter in plan.Parameters) command.Parameters.AddWithValue(parameter.Key, parameter.Value ?? DBNull.Value);
        MySqlResult result;
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            result = await ReadResultAsync(reader, 1, 1, token).ConfigureAwait(false);
            if (result.IsTruncated) command.Cancel();
        }
        result = result with { Rows = result.Rows.Take(pageSize).ToArray(), HasMore = !result.IsTruncated && result.Rows.Count > pageSize };
        return result.IsTruncated || !description.SupportsEditing ? result : await AttachEditingAsync(result, token).ConfigureAwait(false);
    }, token);

    /// <summary>读取真实事务遥测，权限不足时明确返回未知。</summary>
    public Task<MySqlSessionState> ReadStateAsync(CancellationToken token = default) => SerializedAsync(() => ReadStateCoreAsync(token), token);
    private async Task<MySqlSessionState> ReadStateCoreAsync(CancellationToken token)
    {
        bool autocommit = Convert.ToInt32(await ScalarAsync("SELECT @@autocommit", token).ConfigureAwait(false), CultureInfo.InvariantCulture) != 0;
        bool? transaction = null;
        try
        {
            // 只有监控明确启用，空结果才可以解释为无活动事务。
            var enabled = await ScalarAsync("SELECT COUNT(*) FROM performance_schema.setup_consumers WHERE NAME='events_transactions_current' AND ENABLED='YES'", token).ConfigureAwait(false);
            var instrument = await ScalarAsync("SELECT COUNT(*) FROM performance_schema.setup_instruments WHERE NAME='transaction' AND ENABLED='YES'", token).ConfigureAwait(false);
            if (Convert.ToInt32(enabled, CultureInfo.InvariantCulture) > 0 && Convert.ToInt32(instrument, CultureInfo.InvariantCulture) > 0)
                transaction = Convert.ToInt32(await ScalarAsync("SELECT COUNT(*) FROM performance_schema.events_transactions_current e JOIN performance_schema.threads t ON e.THREAD_ID=t.THREAD_ID WHERE t.PROCESSLIST_ID=CONNECTION_ID() AND e.STATE='ACTIVE'", token).ConfigureAwait(false), CultureInfo.InvariantCulture) > 0;
        }
        catch (MySqlException) { }
        State = new(autocommit, transaction); return State;
    }
    private async Task<object?> ScalarAsync(string sql, CancellationToken token)
    { using var command = Command(sql); return await command.ExecuteScalarAsync(token).ConfigureAwait(false); }
    /// <summary>显式回滚当前事务，不承诺撤销 DDL 或已提交写入。</summary>
    public Task RollbackAsync(CancellationToken token = default) => SerializedAsync(async () =>
    { using var command = Command("ROLLBACK"); await command.ExecuteNonQueryAsync(token).ConfigureAwait(false); await ReadStateCoreAsync(token).ConfigureAwait(false); return true; }, token);
    /// <summary>取消或等待未完成工作后释放会话资源。</summary>
    public async ValueTask DisposeAsync()
    {
        // 等待正在执行的命令被调用者取消并结束，之后回收转发和证书。
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return; _disposed = true; ConnectionLost = null;
            await connection.DisposeAsync().ConfigureAwait(false);
            if (bridge is not null) await bridge.DisposeAsync().ConfigureAwait(false); foreach (var item in owned) item.Dispose();
        }
        finally { _gate.Release(); }
    }
}
