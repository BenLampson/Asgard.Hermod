namespace Asgard.Hermod.Redis.Tests;

internal sealed class RedisTestDialogs : IRedisWorkspaceDialogs
{
    internal RedisCloseChoice Choice { get; set; }
    internal int ClosePrompts { get; private set; }
    internal int Comparisons { get; private set; }
    internal Func<IRedisSession, int, string, CancellationToken, Task<RedisKeyId?>>? CreateHandler { get; set; }
    public Task<RedisCloseChoice> CloseDirtyAsync(string key, int database) { ClosePrompts++; return Task.FromResult(Choice); }
    public Task<bool> DeleteAsync(string key, int database) => Task.FromResult(true);
    public Task<RedisKeyId?> CreateAsync(IRedisSession session, int database, string prefix, CancellationToken cancellationToken = default) => CreateHandler?.Invoke(session, database, prefix, cancellationToken) ?? Task.FromResult<RedisKeyId?>(null);
    public Task EditAsync(RedisKeyTabViewModel tab, bool append = false) => tab.EditCommand.ExecuteAsync();
    public Task<string?> ExpiryAsync(string key) => Task.FromResult<string?>(null);
    public Task ShowLatestAsync(string key, string preview) { Comparisons++; return Task.CompletedTask; }
}
