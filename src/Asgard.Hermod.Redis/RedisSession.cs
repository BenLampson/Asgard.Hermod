namespace Asgard.Hermod.Redis;

/// <summary>拥有协议连接与证书。读取消只丢弃结果，写取消之后必须核对结果。</summary>
internal sealed class RedisSession(ConnectionMultiplexer connection, RedisSessionOptions options, IReadOnlyList<IServer> servers,
    IReadOnlyList<IDisposable> owned) : IRedisSession
{
    public RedisSessionOptions Options { get; } = options;
    public IReadOnlyList<string> ScanNodes { get; } = servers.Select(server => server.EndPoint.ToString()!).ToArray();

    public async Task<RedisScanPage> ScanAsync(string node, int database, string cursor, string pattern, string type, CancellationToken cancellationToken)
    {
        try
        {
            CheckDatabase(database); cancellationToken.ThrowIfCancellationRequested();
            var server = servers.Single(item => item.EndPoint.ToString() == node);
            var args = new List<object> { cursor, "MATCH", pattern, "COUNT", 100 };
            if (type.Length > 0) { args.Add("TYPE"); args.Add(type); }
            var result = (RedisResult[])(await server.ExecuteAsync(database, "SCAN", args, CommandFlags.DemandMaster).WaitAsync(cancellationToken).ConfigureAwait(false))!;
            var keys = ((RedisResult[])result[1])!.Select(value => new RedisKeyId((byte[])value!)).ToArray();
            return new((string)result[0]!, keys);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { throw RedisException.Safe(error); }
    }

    public Task<RedisSnapshot> ReadAsync(int database, RedisKeyId key, string cursor = "0", long offset = 0, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(database, key, "page", cursor, offset, cancellationToken);
    public Task<RedisSnapshot> ReadEditableAsync(int database, RedisKeyId key, CancellationToken cancellationToken = default) =>
        ReadCoreAsync(database, key, "full", "0", 0, cancellationToken);

    private async Task<RedisSnapshot> ReadCoreAsync(int database, RedisKeyId key, string mode, string cursor, long offset, CancellationToken token)
    {
        try
        {
            var result = (RedisResult[])(await ExecuteAsync(database, "EVAL_RO", [RedisScripts.Read, 1, key.ToBytes(), mode, cursor, offset,
                RedisSnapshot.MaximumEntries, RedisSnapshot.MaximumBytes], token).ConfigureAwait(false))!;
            var entries = ((RedisResult[])result[7])!.Select(item =>
            {
                var values = (RedisResult[])item!;
                return new RedisEntry((byte[])values[0]!, (byte[])values[1]!, (string)values[2]!, (string)values[3]!);
            }).ToArray();
            return new((string)result[0]!, (long)result[1], (long)result[2], entries, (int)result[3] == 1,
                (string)result[4]!, (long)result[5], (int)result[6] == 1, result.Length > 8 && (int)result[8] == 1);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (RedisServerException error) when (mode == "page" && RedisException.Safe(error).Code == "REDIS_PERMISSION")
        {
            // 无脚本读取权限时仍可查看已知键，但非原子读取永远不提供可写基线。
            try { return await RedisReadFallback.ReadAsync(connection.GetDatabase(database), key, cursor, offset, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception failure) { throw RedisException.Safe(failure); }
        }
        catch (Exception error) { throw RedisException.Safe(error); }
    }

    public Task SaveAsync(int database, RedisKeyId key, RedisSnapshot baseline, RedisSnapshot draft, CancellationToken cancellationToken = default) =>
        CompareAsync(database, key, baseline, draft, "save", cancellationToken);
    public Task DeleteAsync(int database, RedisKeyId key, RedisSnapshot baseline, CancellationToken cancellationToken = default) =>
        CompareAsync(database, key, baseline, baseline, "delete", cancellationToken);

    private Task CompareAsync(int database, RedisKeyId key, RedisSnapshot baseline, RedisSnapshot draft, string operation, CancellationToken token)
    {
        if (!baseline.IsComplete || !draft.IsComplete || draft.Entries.Count > RedisSnapshot.MaximumEntries
            || draft.Entries.Sum(entry => (long)entry.Name.Length + entry.Value.Length) > RedisSnapshot.MaximumBytes)
            throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT");
        if (baseline.Entries.Count > RedisSnapshot.MaximumEntries)
            throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT");
        if (operation == "save")
        {
            if (baseline.Type == "none" && draft.Type == "stream")
            { if (draft.Entries.Count == 0) throw new RedisException("RedisDraftInvalid", "REDIS_DRAFT"); }
            else RedisDraftValidation.Validate(draft);
        }
        var args = new List<object> { RedisScripts.CompareWrite, 1, key.ToBytes(), operation, baseline.Type,
            baseline.Entries.Count, draft.Type, draft.Entries.Count, baseline.Length };
        foreach (var entry in baseline.Entries.Concat(draft.Entries)) args.AddRange([entry.Name, entry.Value, entry.Score, entry.RecordId]);
        return WriteAsync(database, args, token);
    }

    public Task SetExpiryAsync(int database, RedisKeyId key, TimeSpan? expiry, CancellationToken cancellationToken = default)
    {
        if (expiry is not null && (expiry.Value.TotalMilliseconds < 1 || expiry.Value.TotalMilliseconds > long.MaxValue))
            throw new RedisException("RedisExpiryInvalid", "REDIS_EXPIRY");
        return WriteAsync(database, [RedisScripts.Expiry, 1, key.ToBytes(), expiry is null ? -1 : checked((long)expiry.Value.TotalMilliseconds)], cancellationToken);
    }

    public Task AppendAsync(int database, RedisKeyId key, IReadOnlyList<RedisEntry> fields, CancellationToken cancellationToken = default)
    {
        if (fields.Count is 0 or > RedisSnapshot.MaximumEntries || fields.Sum(entry => (long)entry.Name.Length + entry.Value.Length) > RedisSnapshot.MaximumBytes)
            throw new RedisException("RedisDraftInvalid", "REDIS_DRAFT");
        var args = new List<object> { RedisScripts.Append, 1, key.ToBytes() };
        foreach (var entry in fields) args.AddRange([entry.Name, entry.Value]);
        return WriteAsync(database, args, cancellationToken);
    }

    private async Task WriteAsync(int database, ICollection<object> args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); bool dispatched = false;
        try
        {
            CheckDatabase(database); dispatched = true;
            long result = (long)await ExecuteAsync(database, "EVAL", args, token).ConfigureAwait(false);
            if (result == 0) throw new RedisException("RedisConflict", "REDIS_CONFLICT");
            if (result == -1) throw new RedisException("RedisPermissionDenied", "REDIS_PERMISSION");
            if (result == -2) throw new RedisException("RedisDraftInvalid", "REDIS_DRAFT");
        }
        catch (Exception error) { throw RedisException.Safe(error, dispatched); }
    }

    private Task<RedisResult> ExecuteAsync(int database, string command, ICollection<object> args, CancellationToken token)
    {
        CheckDatabase(database); token.ThrowIfCancellationRequested();
        return connection.GetDatabase(database).ExecuteAsync(command, args, CommandFlags.DemandMaster).WaitAsync(token);
    }
    private void CheckDatabase(int database)
    {
        if (database < 0 || Options.Topology == RedisIds.Cluster && database != 0) throw Text.Exception("RedisClusterDatabaseZero");
    }
    public ValueTask DisposeAsync()
    {
        connection.Dispose(); foreach (var item in owned) item.Dispose(); return ValueTask.CompletedTask;
    }
}
