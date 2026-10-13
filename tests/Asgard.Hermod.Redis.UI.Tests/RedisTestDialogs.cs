namespace Asgard.Hermod.Redis.Tests;

internal sealed class RedisTestDialogs : IRedisWorkspaceDialogs
{
    internal RedisCloseChoice Choice { get; set; }
    internal int ClosePrompts { get; private set; }
    internal int Comparisons { get; private set; }
    public Task<RedisCloseChoice> CloseDirtyAsync(string key, int database) { ClosePrompts++; return Task.FromResult(Choice); }
    public Task<bool> DeleteAsync(string key, int database) => Task.FromResult(true);
    public Task<RedisCreateRequest?> CreateAsync() => Task.FromResult<RedisCreateRequest?>(null);
    public Task<string?> ExpiryAsync(string key) => Task.FromResult<string?>(null);
    public Task ShowLatestAsync(string key, string preview) { Comparisons++; return Task.CompletedTask; }
}
