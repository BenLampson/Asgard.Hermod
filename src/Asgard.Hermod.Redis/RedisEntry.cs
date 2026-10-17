namespace Asgard.Hermod.Redis;

/// <summary>集合字段、成员或 Stream 字段保持原始字节；分数以 invariant 文本传输。</summary>
public sealed record RedisEntry(byte[] Name, byte[] Value, string Score = "", string RecordId = "");
