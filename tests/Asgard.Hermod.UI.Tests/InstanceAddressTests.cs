namespace Asgard.Hermod.UI.Tests;

/// <summary>安全地址投影不泄漏用户信息、令牌、密码或未知协议内容。</summary>
public sealed class InstanceAddressTests
{
    [Theory]
    [InlineData("mysql", "host", "db.internal", "db.internal:3306")]
    [InlineData("postgresql", "host", "pg.internal", "pg.internal:5432")]
    [InlineData("postgresql", "host", "[::1]", "[::1]:5432")]
    [InlineData("postgresql", "host", "user:password@pg.internal", "")]
    [InlineData("postgresql", "host", "host;Password=secret", "")]
    [InlineData("ssh", "host", "10.0.0.1", "10.0.0.1:22")]
    [InlineData("redis", "host", "[::1]", "[::1]:6379")]
    [InlineData("mysql", "host", "user:password@db.internal", "")]
    [InlineData("mysql", "host", "host;Password=secret", "")]
    [InlineData("storage", "endpoint", "https://user:password@storage.test:9443/private?token=secret#fragment", "https://storage.test:9443")]
    [InlineData("storage", "endpoint", "oss-cn-hangzhou.aliyuncs.com", "https://oss-cn-hangzhou.aliyuncs.com")]
    [InlineData("storage", "endpoint", "file:///private/key", "")]
    public void Address_OnlyIncludesAllowlistedComponents(string type, string key, string value, string expected) =>
        Assert.Equal(expected, InstanceAddress.Format(type, new Dictionary<string, string> { [key] = value, ["password"] = "must-not-display" }));

    [Theory]
    [InlineData("redis.sentinel", "sentinel-a, sentinel-b:26380", "sentinel-a:26379, sentinel-b:26380")]
    [InlineData("redis.cluster", "node-a:6379\nnode-b:6380", "node-a:6379, node-b:6380")]
    [InlineData("redis.cluster", "node-a:6379,user:password@node-b:6380", "")]
    public void RedisTopology_UsesNodeEntryFieldsWithoutCredentials(string provider, string nodes, string expected) =>
        Assert.Equal(expected, InstanceAddress.Format("redis", new Dictionary<string, string> { ["nodes"] = nodes }, provider));
}
