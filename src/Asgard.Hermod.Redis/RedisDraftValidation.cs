namespace Asgard.Hermod.Redis;

/// <summary>先验证完整参数，避免原子脚本修改后遇到不合法分数或重复字段。</summary>
public static class RedisDraftValidation
{
    /// <summary>仅支持可安全替换的类型，Stream 使用单独追加入口。</summary>
    public static void Validate(RedisSnapshot draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        bool invalid = draft.Type is not ("string" or "hash" or "list" or "set" or "zset")
            || draft.Entries.Count == 0 || draft.Type == "string" && draft.Entries.Count != 1;
        if (draft.Type is "hash" or "set" or "zset")
            invalid |= draft.Entries.Select(entry => Convert.ToBase64String(entry.Name)).Distinct(StringComparer.Ordinal).Count() != draft.Entries.Count;
        if (draft.Type == "zset") invalid |= draft.Entries.Any(entry => !double.TryParse(entry.Score, NumberStyles.Float, CultureInfo.InvariantCulture, out double score) || !double.IsFinite(score));
        if (invalid) throw new RedisException("RedisDraftInvalid", "REDIS_DRAFT");
    }
}
