namespace Asgard.Hermod.Redis.UI.ViewModels;

/// <summary>最新值展示与比较不改变编辑状态。</summary>
internal static class RedisPreview
{
    internal static string Format(RedisSnapshot snapshot, string format)
    {
        if (snapshot.Type == "none") return Text.Get("RedisMissing");
        if (snapshot.Type == "string")
        {
            var value = snapshot.Entries.FirstOrDefault()?.Value ?? [];
            try { return RedisValueCodec.Encode(value, format, true); }
            catch { return Convert.ToBase64String(value); }
        }
        return string.Join(Environment.NewLine, snapshot.Entries.Select(entry => entry.RecordId + " " + Convert.ToBase64String(entry.Name)
            + "\t" + Convert.ToBase64String(entry.Value) + "\t" + entry.Score));
    }
    internal static bool Equal(RedisSnapshot left, RedisSnapshot right)
    {
        if (left.Type != right.Type || left.Entries.Count != right.Entries.Count) return false;
        string Identity(RedisEntry entry) => Convert.ToBase64String(entry.Name);
        var first = left.Type == "list" ? left.Entries : left.Entries.OrderBy(Identity).ToArray();
        var second = right.Type == "list" ? right.Entries : right.Entries.OrderBy(Identity).ToArray();
        return first.Zip(second).All(pair => (left.Type == "list" || pair.First.Name.AsSpan().SequenceEqual(pair.Second.Name))
            && pair.First.Value.AsSpan().SequenceEqual(pair.Second.Value) && pair.First.Score == pair.Second.Score && pair.First.RecordId == pair.Second.RecordId);
    }
}
