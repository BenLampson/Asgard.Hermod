using System.Diagnostics;

namespace Asgard.Hermod.PostgreSql;

internal sealed partial class PostgreSqlSession
{
    /// <inheritdoc />
    public Task<PostgreSqlExecution> ExecuteReadOnlyAsync(string sql, string database, CancellationToken token = default) => SerializedAsync(async () =>
    {
        ReadOnlySqlQuery.Validate(sql);
        RequireDatabase(database);
        if (_hasExecutedUserSql || PostgreSqlSql.Split(sql, StandardStrings).Count != 1) throw Text.Exception("SqlAgentReadOnlyRequired");
        _hasExecutedUserSql = true;
        var watch = Stopwatch.StartNew();
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token).ConfigureAwait(false);
        using (var readOnly = Command("SET TRANSACTION READ ONLY")) await readOnly.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        using var command = Command(sql);
        PostgreSqlResult result;
        await using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess, token).ConfigureAwait(false))
        {
            result = await ReadResultAsync(reader, 1, 1, token).ConfigureAwait(false);
            if (result.IsTruncated) command.Cancel();
        }
        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        return new PostgreSqlExecution([result], [new(1, 0, true)], null, watch.Elapsed, new(true, false));
    }, token);
}
