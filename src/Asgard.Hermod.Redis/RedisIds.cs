namespace Asgard.Hermod.Redis;

/// <summary>Redis 配置与操作使用稳定协议标识，不比较翻译后的标签。</summary>
public static class RedisIds
{
    /// <summary>实例类型。</summary>
    public const string Type = "redis";
    /// <summary>单节点连接器。</summary>
    public const string Standalone = "redis.standalone";
    /// <summary>哨兵连接器。</summary>
    public const string Sentinel = "redis.sentinel";
    /// <summary>集群连接器。</summary>
    public const string Cluster = "redis.cluster";
}
