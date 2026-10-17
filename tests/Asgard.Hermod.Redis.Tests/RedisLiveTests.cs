using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Ssh;
using StackExchange.Redis;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>仅显式指定测试端口时运行；每项测试只操作自己的随机键。</summary>
public sealed partial class RedisLiveTests
{
    public static bool IsEnabled => int.TryParse(Environment.GetEnvironmentVariable("HERMOD_REDIS_TEST_PORT"), out _);
    private static string Endpoint => "127.0.0.1:" + Environment.GetEnvironmentVariable("HERMOD_REDIS_TEST_PORT");
    private static InstanceConnectionOptions Options => new()
    {
        TypeId = RedisIds.Type,
        ProviderId = RedisIds.Standalone,
        Parameters = new Dictionary<string, string> { ["host"] = "127.0.0.1", ["port"] = Environment.GetEnvironmentVariable("HERMOD_REDIS_TEST_PORT")! }
    };

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task StringSave_PreservesAbsoluteExpiryAndDetectsConflict()
    {
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase();
        var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            await db.StringSetAsync(key.ToBytes(), "first", TimeSpan.FromMinutes(1));
            long expiry = (long)await db.ExecuteAsync("PEXPIRETIME", key.ToBytes());
            var baseline = await session.ReadEditableAsync(0, key, cancellationToken: TestContext.Current.CancellationToken);
            await session.SaveAsync(0, key, baseline, baseline with { Entries = [new([], "next"u8.ToArray())] }, cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("next", await db.StringGetAsync(key.ToBytes()));
            Assert.Equal(expiry, (long)await db.ExecuteAsync("PEXPIRETIME", key.ToBytes()));
            var conflict = await Assert.ThrowsAsync<RedisException>(() => session.SaveAsync(0, key, baseline, baseline, cancellationToken: TestContext.Current.CancellationToken));
            Assert.Equal("REDIS_CONFLICT", conflict.Code); Assert.Equal("next", await db.StringGetAsync(key.ToBytes()));
            await db.KeyDeleteAsync(key.ToBytes());
            await Assert.ThrowsAsync<RedisException>(() => session.SetExpiryAsync(0, key, TimeSpan.FromMinutes(1), cancellationToken: TestContext.Current.CancellationToken));
            Assert.False(await db.KeyExistsAsync(key.ToBytes()));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Theory(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    [InlineData("hash")]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("zset")]
    public async Task CollectionCreateSaveAndDelete_AreGuarded(string type)
    {
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase();
        var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            var draft = new RedisSnapshot(type, 1, -1, [new("field"u8.ToArray(), "value"u8.ToArray(), "1.5")], true);
            await session.SaveAsync(0, key, RedisSnapshot.Missing, draft, cancellationToken: TestContext.Current.CancellationToken);
            await Assert.ThrowsAsync<RedisException>(() => session.SaveAsync(0, key, RedisSnapshot.Missing, draft, cancellationToken: TestContext.Current.CancellationToken));
            var baseline = await session.ReadEditableAsync(0, key, cancellationToken: TestContext.Current.CancellationToken); Assert.True(baseline.IsComplete); Assert.Equal(type, baseline.Type);
            await session.SaveAsync(0, key, baseline, draft with { Entries = [new("other"u8.ToArray(), "new"u8.ToArray(), "-2")] }, cancellationToken: TestContext.Current.CancellationToken);
            var changed = await session.ReadEditableAsync(0, key, cancellationToken: TestContext.Current.CancellationToken); Assert.Single(changed.Entries);
            await session.DeleteAsync(0, key, changed, cancellationToken: TestContext.Current.CancellationToken); Assert.False(await db.KeyExistsAsync(key.ToBytes()));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task StreamAppend_DoesNotTrimAndScanKeepsCursor()
    {
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase();
        var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options, cancellationToken: TestContext.Current.CancellationToken);
        try
        {
            await session.AppendAsync(0, key, [new("field"u8.ToArray(), "value"u8.ToArray())], cancellationToken: TestContext.Current.CancellationToken);
            await session.AppendAsync(0, key, [new("field"u8.ToArray(), "next"u8.ToArray())], cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(2, await db.StreamLengthAsync(key.ToBytes()));
            var snapshot = await session.ReadEditableAsync(0, key, cancellationToken: TestContext.Current.CancellationToken); Assert.Equal(2, snapshot.Length); Assert.True(snapshot.IsComplete);
            var page = await session.ScanAsync(session.ScanNodes[0], 0, "0", "hermod-test:*", "stream", TestContext.Current.CancellationToken);
            Assert.NotNull(page.Cursor);
            await session.DeleteAsync(0, key, snapshot, cancellationToken: TestContext.Current.CancellationToken); Assert.False(await db.KeyExistsAsync(key.ToBytes()));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }

    private sealed class UnusedSsh : ISshTcpStreamFactory
    {
        public Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
