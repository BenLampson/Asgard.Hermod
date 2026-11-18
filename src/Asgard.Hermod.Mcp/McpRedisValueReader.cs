using System.Text;
using Asgard.Hermod.Redis;

namespace Asgard.Hermod.Mcp;

/// <summary>读取已确认键的有界内容，客户端游标不暴露 Redis 原始游标或节点信息。</summary>
internal static class McpRedisValueReader
{
    private sealed record Position(RedisSnapshot? Pending, int Index, string Cursor, long Offset);

    internal static async Task<object> ReadAsync(IRedisSession session, int database, string key, string grantId,
        string cursor, McpCursorCache cursors, CancellationToken token)
    {
        var position = cursors.Read<Position>(cursor, McpToolCatalog.RedisReadValue, grantId, key);
        var snapshot = position?.Pending ?? await session.ReadAsync(database, RedisKeyId.FromText(key),
            position?.Cursor ?? "0", position?.Offset ?? 0, token).ConfigureAwait(false);
        int index = position?.Pending is null ? 0 : position.Index;
        int budget = 12 * 1024;
        bool truncated = snapshot.IsTruncated;
        var entries = new List<object>();
        while (index < snapshot.Entries.Count && entries.Count < 100 && budget > 0)
        {
            var entry = snapshot.Entries[index++];
            var name = Value(entry.Name, ref budget, ref truncated);
            var value = Value(entry.Value, ref budget, ref truncated);
            entries.Add(new { name, value, score = McpJson.Clip(entry.Score, 128), recordId = McpJson.Clip(entry.RecordId, 128) });
        }
        Position? next = index < snapshot.Entries.Count ? new(snapshot, index, snapshot.Cursor, snapshot.Offset)
            : snapshot.HasMore ? new(null, 0, snapshot.Cursor, snapshot.Offset) : null;
        return new
        {
            database,
            key,
            snapshot.Type,
            snapshot.Length,
            snapshot.TtlMilliseconds,
            entries,
            truncated,
            nextCursor = next is null ? null : cursors.Store(McpToolCatalog.RedisReadValue, grantId, key, next)
        };
    }

    private static object Value(byte[] bytes, ref int budget, ref bool truncated)
    {
        int count = Math.Min(bytes.Length, budget);
        budget -= count;
        truncated |= count < bytes.Length;
        var selected = bytes.AsSpan(0, count);
        try
        {
            string text = new UTF8Encoding(false, true).GetString(selected);
            if (!text.Any(value => char.IsControl(value) && value is not ('\t' or '\r' or '\n')))
                return new { encoding = "utf8", data = text, truncated = count < bytes.Length };
        }
        catch (DecoderFallbackException) { }
        return new { encoding = "base64", data = Convert.ToBase64String(selected), truncated = count < bytes.Length };
    }
}
