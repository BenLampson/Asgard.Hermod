using System.Globalization;
using Asgard.Hermod.Abstractions.Models;
using Asgard.Hermod.Core;
using Xunit;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>语言、复合条件与拓扑校验不依赖开发机器环境。</summary>
public sealed class RedisProfileTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("zh-Hans")]
    [InlineData("fr-FR")]
    public void Profiles_RegisterInEveryLanguageAndHideInapplicableSecrets(string language)
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
            var fields = RedisProfile.Fields(RedisIds.Standalone);
            var password = fields.Single(field => field.Key == "ssh.password");
            Assert.False(InstanceFieldValidation.IsApplicable(password, new Dictionary<string, string> { ["route"] = "direct", ["ssh.auth"] = "password" }));
            Assert.False(InstanceFieldValidation.IsApplicable(password, new Dictionary<string, string> { ["route"] = "ssh", ["ssh.auth"] = "agent" }));
            Assert.True(InstanceFieldValidation.IsApplicable(password, new Dictionary<string, string> { ["route"] = "ssh", ["ssh.auth"] = "password" }));
            var registry = new InstanceProviderRegistry(new[] { RedisIds.Standalone, RedisIds.Sentinel, RedisIds.Cluster }.Select(topology => new RedisInstanceProvider(topology, new UnusedFactory())));
            Assert.Equal(3, registry.GetProviders(RedisIds.Type).Count);
            Assert.DoesNotContain(fields, field => field.Key.Contains("creation", StringComparison.Ordinal));
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Theory]
    [InlineData("[::1]:6380", "::1", 6380)]
    [InlineData("::1", "::1", 6379)]
    [InlineData("localhost:6379", "localhost", 6379)]
    public void Endpoint_ParsesWithoutCredentials(string text, string host, int port) => Assert.Equal(new RedisEndpoint(host, port), RedisEndpoint.Parse(text));

    [Theory]
    [InlineData("redis://host")]
    [InlineData("user@host:6379")]
    [InlineData("host:0")]
    [InlineData("host:65536")]
    [InlineData("[::1]:")]
    public void Endpoint_RejectsInvalidConfiguration(string text) => Assert.Throws<InvalidOperationException>(() => RedisEndpoint.Parse(text));

    [Fact]
    public void Cluster_RejectsNonzeroDatabaseAndSsh()
    {
        Assert.Throws<InvalidOperationException>(() => RedisSessionOptions.Parse(new()
        {
            TypeId = RedisIds.Type,
            ProviderId = RedisIds.Cluster,
            Parameters = new Dictionary<string, string> { ["nodes"] = "localhost", ["database"] = "1" }
        }));
        Assert.Throws<InvalidOperationException>(() => RedisSessionOptions.Parse(new()
        {
            TypeId = RedisIds.Type,
            ProviderId = RedisIds.Cluster,
            Parameters = new Dictionary<string, string> { ["nodes"] = "localhost", ["route"] = "ssh" }
        }));
    }

    [Fact]
    public void BinaryKeys_PreserveIdentityAndDoNotGroupInvalidUtf8()
    {
        var empty = new RedisKeyId([]); Assert.False(empty.IsText); Assert.Equal("base64:", empty.DisplayName);
        byte[] bytes = [0xff, 0x3a, 0x01]; var key = new RedisKeyId(bytes); bytes[0] = 0;
        Assert.False(key.IsText); Assert.Equal(new RedisKeyId([0xff, 0x3a, 0x01]), key);
        Assert.StartsWith("base64:", key.DisplayName); Assert.Equal(new byte[] { 0xff, 0x3a, 0x01 }, key.ToBytes());
    }

    [Fact]
    public void Draft_RejectsDuplicateMembersAndNonfiniteScores()
    {
        Assert.Throws<RedisException>(() => RedisDraftValidation.Validate(new("set", 2, -1, [new([1], []), new([1], [])], true)));
        Assert.Throws<RedisException>(() => RedisDraftValidation.Validate(new("zset", 1, -1, [new([1], [], "NaN")], true)));
    }

    private sealed class UnusedFactory : IRedisSessionFactory
    {
        public Task<IRedisSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
