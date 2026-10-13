using Asgard.Hermod.Abstractions.Models;
using StackExchange.Redis;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>通过独立键和临时 ACL 身份验证真实服务器边界。</summary>
public sealed partial class RedisLiveTests
{
    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task HashEdits_CompareOnlyChangedFieldsAndPreserveUnrelatedChanges()
    {
        var token=TestContext.Current.CancellationToken;
        using var direct=await ConnectionMultiplexer.ConnectAsync(Endpoint);var db=direct.GetDatabase();
        var key=RedisKeyId.FromText("hermod-test:"+Guid.NewGuid().ToString("N"));
        await using var session=await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options,token);
        try
        {
            await db.HashSetAsync(key.ToBytes(),[new("a","first"),new("b","unrelated")]);
            var baseline=await session.ReadEditableAsync(0,key,token);
            var edited=baseline with { Entries=baseline.Entries.Select(entry=>entry.Name.AsSpan().SequenceEqual("a"u8)
                ? entry with { Value="edited"u8.ToArray() } : entry).ToArray() };
            await db.HashSetAsync(key.ToBytes(),"b","external");await db.HashSetAsync(key.ToBytes(),"c","external-added");
            await session.SaveAsync(0,key,baseline,edited,token);
            Assert.Equal("external",await db.HashGetAsync(key.ToBytes(),"b"));Assert.Equal("external-added",await db.HashGetAsync(key.ToBytes(),"c"));
            Assert.Equal("edited",await db.HashGetAsync(key.ToBytes(),"a"));
            await Assert.ThrowsAsync<RedisException>(()=>session.SaveAsync(0,key,baseline,edited,token));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task RestrictedScanAndScriptRights_AllowKnownKeyReadWithoutPretendingItIsEditable()
    {
        var token=TestContext.Current.CancellationToken;
        using var direct=await ConnectionMultiplexer.ConnectAsync(Endpoint);var db=direct.GetDatabase();
        string user="hermod-test-"+Guid.NewGuid().ToString("N");var key=RedisKeyId.FromText("hermod-test:"+Guid.NewGuid().ToString("N"));
        try
        {
            await db.StringSetAsync(key.ToBytes(),"readable");
            await db.ExecuteAsync("ACL","SETUSER",user,"on",">isolated-test-only","~hermod-test:*","+@all","-scan","-eval_ro","-eval","-evalsha_ro","-evalsha");
            var values=new Dictionary<string,string>(Options.Parameters) { ["auth"]="acl",["user"]=user,["acl.password"]="isolated-test-only" };
            var options=new InstanceConnectionOptions { TypeId=RedisIds.Type,ProviderId=RedisIds.Standalone,Parameters=values };
            var result=await new RedisInstanceProvider(RedisIds.Standalone,new RedisSessionFactory(new UnusedSsh())).TestConnectionAsync(options,token);
            Assert.True(result.IsSuccessful);Assert.False(result.Checks!.Last().IsSuccessful);
            await using var session=await new RedisSessionFactory(new UnusedSsh()).OpenAsync(options,token);
            var read=await session.ReadAsync(0,key,cancellationToken:token);Assert.False(read.IsComplete);Assert.Equal("readable"u8.ToArray(),Assert.Single(read.Entries).Value);
            var error=await Assert.ThrowsAsync<RedisException>(()=>session.ReadEditableAsync(0,key,token));Assert.Equal("REDIS_PERMISSION",error.Code);
        }
        finally { await db.ExecuteAsync("ACL","DELUSER",user);await db.KeyDeleteAsync(key.ToBytes()); }
    }

    [Fact(SkipUnless = nameof(IsEnabled), Skip = "Set HERMOD_REDIS_TEST_PORT to run isolated live tests.")]
    public async Task LargeValue_IsBoundedReadOnlyAndListIndexDriftCannotOverwrite()
    {
        var token=TestContext.Current.CancellationToken;
        using var direct=await ConnectionMultiplexer.ConnectAsync(Endpoint);var db=direct.GetDatabase();
        var key=RedisKeyId.FromText("hermod-test:"+Guid.NewGuid().ToString("N"));
        await using var session=await new RedisSessionFactory(new UnusedSsh()).OpenAsync(Options,token);
        try
        {
            await db.StringSetAsync(key.ToBytes(),new byte[RedisSnapshot.MaximumBytes+1]);
            var large=await session.ReadEditableAsync(0,key,token);Assert.False(large.IsComplete);Assert.True(large.IsTruncated);
            Assert.Equal(RedisSnapshot.MaximumBytes,Assert.Single(large.Entries).Value.Length);
            await Assert.ThrowsAsync<RedisException>(()=>session.SaveAsync(0,key,large,large,token));
            await db.KeyDeleteAsync(key.ToBytes());await db.ListRightPushAsync(key.ToBytes(),["a","b"]);
            var baseline=await session.ReadEditableAsync(0,key,token);await db.ListLeftPushAsync(key.ToBytes(),"external");
            var draft=baseline with { Entries=[new([],"changed"u8.ToArray()),baseline.Entries[1]] };
            var conflict=await Assert.ThrowsAsync<RedisException>(()=>session.SaveAsync(0,key,baseline,draft,token));Assert.Equal("REDIS_CONFLICT",conflict.Code);
            Assert.Equal(new RedisValue[] {"external","a","b"},await db.ListRangeAsync(key.ToBytes()));
        }
        finally { await db.KeyDeleteAsync(key.ToBytes()); }
    }
}
