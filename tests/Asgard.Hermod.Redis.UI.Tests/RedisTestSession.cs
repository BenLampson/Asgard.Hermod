namespace Asgard.Hermod.Redis.Tests;

/// <summary>协议替身可模拟游标、冲突和取消后迟到结果，不依赖 Redis 网络。</summary>
internal sealed class RedisTestSession : IRedisSession, IRedisSessionFactory
{
    internal RedisSnapshot Snapshot { get; set; } = new("string", 5, -1, [new([], "value"u8.ToArray())], true);
    internal Func<string, string, string, Task<RedisScanPage>>? Scan { get; set; }
    internal Exception? SaveError { get; set; }
    internal int Reads { get; private set; }
    internal int Saves { get; private set; }
    internal List<(string Node, string Cursor, string Pattern)> Calls { get; } = [];
    public RedisSessionOptions Options { get; } = RedisSessionOptions.Parse(Connection);
    public static InstanceConnectionOptions Connection { get; } = new()
    {
        TypeId = RedisIds.Type,
        ProviderId = RedisIds.Standalone,
        Parameters = new Dictionary<string, string> { ["host"] = "localhost" }
    };
    public IReadOnlyList<string> ScanNodes { get; set; } = ["primary"];
    public int DatabaseCount => 16;
    internal Func<int, Task<long?>>? DatabaseKeyCount { get; set; }
    public Task<long?> ReadDatabaseKeyCountAsync(int database, CancellationToken cancellationToken = default) => DatabaseKeyCount?.Invoke(database) ?? Task.FromResult<long?>(0);
    public Task<IRedisSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) => Task.FromResult<IRedisSession>(this);
    public Task<RedisScanPage> ScanAsync(string node, int database, string cursor, string pattern, string type, CancellationToken cancellationToken)
    { Calls.Add((node, cursor, pattern)); return Scan?.Invoke(node, cursor, pattern) ?? Task.FromResult(new RedisScanPage("0", [])); }
    public Task<RedisSnapshot> ReadAsync(int database, RedisKeyId key, string cursor = "0", long offset = 0, CancellationToken cancellationToken = default)
    { Reads++; return Task.FromResult(Snapshot); }
    public Task<RedisSnapshot> ReadEditableAsync(int database, RedisKeyId key, CancellationToken cancellationToken = default)
    { Reads++; return Task.FromResult(Snapshot); }
    public Task SaveAsync(int database, RedisKeyId key, RedisSnapshot baseline, RedisSnapshot draft, CancellationToken cancellationToken = default)
    { Saves++; if (SaveError is not null) return Task.FromException(SaveError); Snapshot = draft; return Task.CompletedTask; }
    public Task DeleteAsync(int database, RedisKeyId key, RedisSnapshot baseline, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CreateAsync(int database, RedisKeyId key, RedisSnapshot initial, TimeSpan? expiry, CancellationToken cancellationToken = default)
    { Saves++; if (SaveError is not null) return Task.FromException(SaveError); Snapshot = initial; return Task.CompletedTask; }
    public Task SetExpiryAsync(int database, RedisKeyId key, TimeSpan? expiry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task AppendAsync(int database, RedisKeyId key, IReadOnlyList<RedisEntry> fields, CancellationToken cancellationToken = default)
    { Saves++; return SaveError is null ? Task.CompletedTask : Task.FromException(SaveError); }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
