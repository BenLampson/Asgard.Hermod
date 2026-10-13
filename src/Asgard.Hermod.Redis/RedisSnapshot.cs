namespace Asgard.Hermod.Redis;

/// <summary>基线只包含真实协议信息，没有 Redis 不提供的创建时间。</summary>
public sealed record RedisSnapshot(string Type, long Length, long TtlMilliseconds, IReadOnlyList<RedisEntry> Entries,
    bool IsComplete, string Cursor = "0", long Offset = 0, bool HasMore = false, bool IsTruncated = false)
{
    /// <summary>字符串和集合的编辑上限，避免长脚本和无限拉取。</summary>
    public const int MaximumEntries = 1000;
    /// <summary>完整编辑值的字节上限。</summary>
    public const int MaximumBytes = 1024 * 1024;
    /// <summary>非存在键的空基线用于 create-if-missing。</summary>
    public static RedisSnapshot Missing { get; } = new("none", 0, -2, [], true);
}
