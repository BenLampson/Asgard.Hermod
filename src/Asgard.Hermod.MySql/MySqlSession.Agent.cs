using System.Diagnostics;

namespace Asgard.Hermod.MySql;

internal sealed partial class MySqlSession
{
    /// <inheritdoc />
    public Task<MySqlExecution> ExecuteReadOnlyAsync(string sql, string database, CancellationToken token = default) => SerializedAsync(async () =>
    {
        ReadOnlySqlQuery.Validate(sql);
        if (_hasExecutedUserSql) throw Text.Exception("SqlAgentReadOnlyUnavailable");
        if (database.Length > 0 && connection.Database != database) await connection.ChangeDatabaseAsync(database, token).ConfigureAwait(false);
        string mode = Convert.ToString(await ScalarAsync("SELECT @@sql_mode", token).ConfigureAwait(false), CultureInfo.InvariantCulture) ?? "";
        var statements = MySqlSql.Split(sql, mode.Split(',').Contains("NO_BACKSLASH_ESCAPES", StringComparer.OrdinalIgnoreCase));
        if (statements.Count != 1) throw Text.Exception("SqlAgentReadOnlyRequired");
        _hasExecutedUserSql = true;
        var watch = Stopwatch.StartNew();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, isReadOnly: true, token).ConfigureAwait(false);
        using var command = Command(statements[0].Sql);
        command.Transaction = transaction;
        MySqlResult result;
        await using (var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false))
        {
            result = await ReadResultAsync(reader, 1, 1, token).ConfigureAwait(false);
            if (result.IsTruncated) command.Cancel();
        }
        // 该连接由调用方立即释放；不提交任何事务，也不为自动查询结果附加网格编辑资格。
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return new MySqlExecution([result], [new(1, 0, true)], null, watch.Elapsed, new(true, false));
    }, token);
}
