using System.Security.Cryptography;

namespace Asgard.Hermod.Mcp;

/// <summary>会话内游标绑定工具、授权和前缀，外部客户端不接触底层令牌或节点地址。</summary>
internal sealed class McpCursorCache
{
    private sealed record Entry(string Tool, string GrantId, string Scope, object Data, long Created);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly Func<long> _clock;

    internal McpCursorCache(Func<long>? clock = null) => _clock = clock ?? (() => Environment.TickCount64);

    internal string Store(string tool, string grantId, string scope, object data)
    {
        lock (_entries)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            if (_entries.Count >= 32) _entries.Remove(_entries.MinBy(item => item.Value.Created).Key);
            _entries.Add(id, new(tool, grantId, scope, data, _clock()));
            return id;
        }
    }

    internal T? Read<T>(string cursor, string tool, string grantId, string scope) where T : class
    {
        if (cursor.Length == 0) return null;
        lock (_entries)
        {
            if (_entries.TryGetValue(cursor, out var entry) && entry.Tool == tool && entry.GrantId == grantId
                && entry.Scope == scope && _clock() - entry.Created <= (long)TimeSpan.FromMinutes(5).TotalMilliseconds
                && entry.Data is T state) return state;
            throw Text.Exception("McpInvalidCursor");
        }
    }

    // 破坏性操作的清单只能领取一次，读取和移除必须在同一把锁里完成。
    internal T Take<T>(string cursor, string tool, string grantId, string scope) where T : class
    {
        lock (_entries)
        {
            var value = Read<T>(cursor, tool, grantId, scope) ?? throw Text.Exception("McpInvalidCursor");
            _entries.Remove(cursor);
            return value;
        }
    }
}
