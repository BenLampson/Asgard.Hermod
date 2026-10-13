using Asgard.Hermod.Abstractions.Models;
using StackExchange.Redis;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>实际 Redis 验证新增值、TTL、创建权限与同名冲突的原子边界。</summary>
public sealed partial class RedisLiveTests
{
    [Theory(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    [InlineData("string")]
    [InlineData("hash")]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("zset")]
    [InlineData("stream")]
    public async Task Create_InitialValueAndExpiryAreAtomicAndExistingKeysArePreserved(string type)
    {
        var token = TestContext.Current.CancellationToken; var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase();
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options, token);
        var entries = type == "string" ? new[] { new RedisEntry([], [255, 0, 1]) } : new[] { new RedisEntry("first"u8.ToArray(), "one"u8.ToArray(), "1.5"), new RedisEntry("second"u8.ToArray(), "two"u8.ToArray(), "-2") };
        var initial = new RedisSnapshot(type, entries.Length, -1, entries, true);
        try
        {
            await session.CreateAsync(0, key, initial, TimeSpan.FromMinutes(1), token);
            long expiry = (long)await db.ExecuteAsync("PEXPIRETIME", key.ToBytes()); Assert.True(expiry > 0);
            var created = await session.ReadEditableAsync(0, key, token); Assert.Equal(type, created.Type);
            byte[] original = (byte[])(await db.ExecuteAsync("DUMP", key.ToBytes()))!;
            Assert.Equal(type == "string" ? 3 : type == "stream" ? 1 : 2, created.Length);
            var conflict = await Assert.ThrowsAsync<RedisException>(() => session.CreateAsync(0, key, new("string", 1, -1, [new([], "overwritten"u8.ToArray())], true), null, token));
            Assert.Equal("REDIS_KEY_EXISTS", conflict.Code); Assert.Equal(expiry, (long)await db.ExecuteAsync("PEXPIRETIME", key.ToBytes()));
            Assert.Equal(type, (string?)await db.ExecuteAsync("TYPE", key.ToBytes()));
            Assert.Equal(original, (byte[])(await db.ExecuteAsync("DUMP", key.ToBytes()))!);
            if (type == "string") Assert.Equal(entries[0].Value, (byte[])(await db.StringGetAsync(key.ToBytes()))!);
            await db.KeyDeleteAsync(key.ToBytes()); await session.CreateAsync(0, key, initial, null, token);
            Assert.Equal(-1, (long)await db.ExecuteAsync("PTTL", key.ToBytes()));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task Create_ExpiryPermissionFailureDoesNotLeaveAPermanentKey()
    {
        var token = TestContext.Current.CancellationToken; string user = "hermod-create-" + Guid.NewGuid().ToString("N");
        var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase();
        try
        {
            await db.ExecuteAsync("ACL", "SETUSER", user, "on", ">isolated-create-only", "~hermod-test:*", "+@all", "-pexpire", "-del", "-dbsize");
            var values = new Dictionary<string, string>(Options.Parameters) { ["auth"] = "acl", ["user"] = user, ["acl.password"] = "isolated-create-only" };
            await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(new InstanceConnectionOptions { TypeId = RedisIds.Type, ProviderId = RedisIds.Standalone, Parameters = values }, token);
            var initial = new RedisSnapshot("string", 1, -1, [new([], "value"u8.ToArray())], true);
            var denied = await Assert.ThrowsAsync<RedisException>(() => session.CreateAsync(0, key, initial, TimeSpan.FromMinutes(1), token));
            Assert.Equal("REDIS_PERMISSION", denied.Code); Assert.False(await db.KeyExistsAsync(key.ToBytes()));
            await session.CreateAsync(0, key, initial, null, token); Assert.True(await db.KeyExistsAsync(key.ToBytes()));
            Assert.Null(await session.ReadDatabaseKeyCountAsync(0, token));
        }
        finally { await db.ExecuteAsync("ACL", "DELUSER", user); await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task DatabaseCount_IsReadFromSelectedDatabaseWithoutScanningValues()
    {
        var token = TestContext.Current.CancellationToken; const int database = 9;
        using var direct = await ConnectionMultiplexer.ConnectAsync(Endpoint); var db = direct.GetDatabase(database);
        await using var session = await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options, token);
        var key = RedisKeyId.FromText("hermod-test:" + Guid.NewGuid().ToString("N"));
        long before = (await session.ReadDatabaseKeyCountAsync(database, token))!.Value;
        try
        {
            await session.CreateAsync(database, key, new("string", 1, -1, [new([], "v"u8.ToArray())], true), null, token);
            Assert.Equal(before + 1, await session.ReadDatabaseKeyCountAsync(database, token));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }
}
