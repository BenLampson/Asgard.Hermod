namespace Asgard.Hermod.Redis;

/// <summary>游标来自服务器，COUNT 只是提示。</summary>
public sealed record RedisScanPage(string Cursor, IReadOnlyList<RedisKeyId> Keys);
