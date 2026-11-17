namespace Asgard.Hermod.Redis;

/// <summary>仅键类型及剩余毫秒数；null 表示永久或键不存在，后者的类型为 none。</summary>
public sealed record RedisKeyMetadata(string Type, long? TtlMilliseconds);
