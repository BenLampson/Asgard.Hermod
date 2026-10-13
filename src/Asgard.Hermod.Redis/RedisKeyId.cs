namespace Asgard.Hermod.Redis;

/// <summary>键身份保存原始字节，显示文本不能用于协议、去重或删除。</summary>
public sealed class RedisKeyId : IEquatable<RedisKeyId>
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly byte[] _bytes;
    /// <summary>复制输入，防止调用方修改已经打开标签的键身份。</summary>
    public RedisKeyId(ReadOnlySpan<byte> bytes) => _bytes = bytes.ToArray();
    /// <summary>用户输入的完整文本键。</summary>
    public static RedisKeyId FromText(string value) => new(StrictUtf8.GetBytes(value));
    /// <summary>协议调用使用副本。</summary>
    public byte[] ToBytes() => _bytes.ToArray();
    /// <summary>无损 UTF-8 名称，二进制键明确显示 Base64。</summary>
    public string DisplayName
    {
        get { return IsText ? StrictUtf8.GetString(_bytes) : "base64:" + Convert.ToBase64String(_bytes); }
    }
    /// <summary>只允许无损文本参与冒号树分组，二进制键作为独立叶节点。</summary>
    public bool IsText { get { try { return !StrictUtf8.GetString(_bytes).Any(char.IsControl); } catch (DecoderFallbackException) { return false; } } }
    /// <summary>每个标签的稳定键标识。</summary>
    public string Identity => Convert.ToBase64String(_bytes);
    /// <inheritdoc />
    public bool Equals(RedisKeyId? other) => other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RedisKeyId key && Equals(key);
    /// <inheritdoc />
    public override int GetHashCode() { var hash = new HashCode(); foreach (byte value in _bytes) hash.Add(value); return hash.ToHashCode(); }
    /// <inheritdoc />
    public override string ToString() => DisplayName;
}
