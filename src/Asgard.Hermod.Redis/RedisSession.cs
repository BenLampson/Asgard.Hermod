namespace Asgard.Hermod.Redis;

/// <summary>拥有协议连接与证书。读取消只丢弃结果，写取消之后必须核对结果。</summary>
internal sealed class RedisSession(ConnectionMultiplexer connection, RedisSessionOptions options, IReadOnlyList<IServer> servers,
    IReadOnlyList<IDisposable> owned) : IRedisSession
{
    private bool _disposed;
    private EventHandler? _connectionLost;
    /// <inheritdoc />
    public bool IsConnected => !_disposed && connection.IsConnected;
    /// <inheritdoc />
    public event EventHandler? ConnectionLost
    {
        add
        {
            if (_connectionLost is null) { connection.ConnectionFailed += OnConnectionFailed; connection.ErrorMessage += OnErrorMessage; }
            _connectionLost += value;
        }
        remove
        {
            _connectionLost -= value;
            if (_connectionLost is null) { connection.ConnectionFailed -= OnConnectionFailed; connection.ErrorMessage -= OnErrorMessage; }
        }
    }
    private void OnConnectionFailed(object? sender, ConnectionFailedEventArgs args)
    { if (!_disposed) _connectionLost?.Invoke(this, EventArgs.Empty); }
    private void OnErrorMessage(object? sender, RedisErrorEventArgs args)
    {
        // NOAUTH/WRONGPASS 是认证失效；对象权限 NOPERM 不代表连接中断，也不向界面暴露错误原文。
        if (!_disposed && (args.Message.StartsWith("NOAUTH", StringComparison.OrdinalIgnoreCase) || args.Message.StartsWith("WRONGPASS", StringComparison.OrdinalIgnoreCase)))
            _connectionLost?.Invoke(this, EventArgs.Empty);
    }
    private void ReportConnectionFailure(Exception error)
    {
        if (_disposed) return;
        if (error is RedisConnectionException or RedisTimeoutException || error is RedisServerException server &&
            (server.Message.StartsWith("NOAUTH", StringComparison.Ordinal) || server.Message.StartsWith("WRONGPASS", StringComparison.Ordinal)))
            _connectionLost?.Invoke(this, EventArgs.Empty);
    }
    public RedisSessionOptions Options { get; } = options;
    // Sentinel 切主或 Cluster 更新后，下一次查询应使用 SDK 当前发现的主节点。
    public IReadOnlyList<string> ScanNodes => connection.GetServers().Where(server => !server.IsReplica && server.ServerType != ServerType.Sentinel)
        .Select(server => server.EndPoint.ToString()!).ToArray();
    public int DatabaseCount => Options.Topology == RedisIds.Cluster ? 1 : servers[0].DatabaseCount;

    public async Task<long?> ReadDatabaseKeyCountAsync(int database, CancellationToken cancellationToken = default)
    {
        try
        {
            CheckDatabase(database); cancellationToken.ThrowIfCancellationRequested();
            var primaries = connection.GetServers().Where(server => !server.IsReplica && server.ServerType != ServerType.Sentinel).ToArray();
            if (primaries.Length == 0) return null;
            long[] counts = await Task.WhenAll(primaries.Select(server => server.DatabaseSizeAsync(database, CommandFlags.DemandMaster))).WaitAsync(cancellationToken).ConfigureAwait(false);
            return counts.Sum();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { ReportConnectionFailure(error); return null; }
    }

    public async Task<RedisScanPage> ScanAsync(string node, int database, string cursor, string pattern, string type, CancellationToken cancellationToken)
    {
        try
        {
            CheckDatabase(database); cancellationToken.ThrowIfCancellationRequested();
            var server = connection.GetServers().Single(item => item.EndPoint.ToString() == node);
            if (server.IsReplica) throw new RedisException("RedisTopologyFailed", "REDIS_TOPOLOGY");
            var args = new List<object> { cursor, "MATCH", pattern, "COUNT", 100 };
            if (type.Length > 0) { args.Add("TYPE"); args.Add(type); }
            var result = (RedisResult[])(await server.ExecuteAsync(database, "SCAN", args, CommandFlags.DemandMaster).WaitAsync(cancellationToken).ConfigureAwait(false))!;
            var keys = ((RedisResult[])result[1])!.Select(value => new RedisKeyId((byte[])value!)).ToArray();
            return new((string)result[0]!, keys);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { ReportConnectionFailure(error); throw RedisException.Safe(error); }
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
            catch (Exception failure) { ReportConnectionFailure(failure); throw RedisException.Safe(failure); }
        }
        catch (Exception error) { ReportConnectionFailure(error); throw RedisException.Safe(error); }
    }

    public Task SaveAsync(int database, RedisKeyId key, RedisSnapshot baseline, RedisSnapshot draft, CancellationToken cancellationToken = default) =>
        CompareAsync(database, key, baseline, draft, "save", cancellationToken);
    public Task DeleteAsync(int database, RedisKeyId key, RedisSnapshot baseline, CancellationToken cancellationToken = default) =>
        CompareAsync(database, key, baseline, baseline, "delete", cancellationToken);

    public Task CreateAsync(int database, RedisKeyId key, RedisSnapshot initial, TimeSpan? expiry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (!initial.IsComplete || initial.Entries.Count > RedisSnapshot.MaximumEntries
            || initial.Entries.Sum(entry => (long)entry.Name.Length + entry.Value.Length) > RedisSnapshot.MaximumBytes)
            throw new RedisException("RedisValueTooLarge", "REDIS_LIMIT");
        RedisDraftValidation.Validate(initial.Type == "stream" ? initial with { Type = "hash" } : initial);
        if (expiry is not null && expiry.Value.TotalMilliseconds < 1) throw new RedisException("RedisExpiryInvalid", "REDIS_EXPIRY");
        var args = new List<object> { RedisScripts.Create, 1, key.ToBytes(), initial.Type,
            expiry is null ? -1 : checked((long)expiry.Value.TotalMilliseconds), initial.Entries.Count };
        foreach (var entry in initial.Entries) args.AddRange([entry.Name, entry.Value, entry.Score]);
        return WriteAsync(database, args, cancellationToken, creating: true);
    }

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

    private async Task WriteAsync(int database, ICollection<object> args, CancellationToken token, bool creating = false)
    {
        token.ThrowIfCancellationRequested(); bool dispatched = false;
        try
        {
            CheckDatabase(database); dispatched = true;
            long result = (long)await ExecuteAsync(database, "EVAL", args, token).ConfigureAwait(false);
            if (result == 0) throw creating ? new RedisException("RedisKeyAlreadyExists", "REDIS_KEY_EXISTS") : new RedisException("RedisConflict", "REDIS_CONFLICT");
            if (result == -1) throw new RedisException("RedisPermissionDenied", "REDIS_PERMISSION");
            if (result == -2) throw new RedisException("RedisDraftInvalid", "REDIS_DRAFT");
        }
        catch (Exception error) { if (!token.IsCancellationRequested) ReportConnectionFailure(error); throw RedisException.Safe(error, dispatched); }
    }

    private Task<RedisResult> ExecuteAsync(int database, string command, ICollection<object> args, CancellationToken token)
    {
        CheckDatabase(database); token.ThrowIfCancellationRequested();
        if (command is "EVAL" or "EVAL_RO")
        {
            var items = args.ToArray();
            RedisValue Value(object value) => value switch
            {
                byte[] bytes => bytes,
                string text => text,
                int number => number,
                long number => number,
                _ => throw new InvalidOperationException()
            };
            var keys = new RedisKey[] { (byte[])items[2] };
            var values = items.Skip(3).Select(Value).ToArray();
            // 专用 SDK 入口保留 Cluster 键槽路由；通用 Execute 的 byte[] 不声明键身份。
            return (command == "EVAL_RO" ? connection.GetDatabase(database).ScriptEvaluateReadOnlyAsync((string)items[0], keys, values, CommandFlags.DemandMaster)
                : connection.GetDatabase(database).ScriptEvaluateAsync((string)items[0], keys, values, CommandFlags.DemandMaster)).WaitAsync(token);
        }
        return connection.GetDatabase(database).ExecuteAsync(command, args, CommandFlags.DemandMaster).WaitAsync(token);
    }
    private void CheckDatabase(int database)
    {
        if (database < 0 || Options.Topology == RedisIds.Cluster && database != 0) throw Text.Exception("RedisClusterDatabaseZero");
    }
    public ValueTask DisposeAsync()
    {
        _disposed = true; connection.ConnectionFailed -= OnConnectionFailed; connection.ErrorMessage -= OnErrorMessage; _connectionLost = null;
        connection.Dispose(); foreach (var item in owned) item.Dispose(); return ValueTask.CompletedTask;
    }
}
