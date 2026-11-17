using System.Security.Cryptography;

namespace Asgard.Hermod.Mcp;

/// <summary>会话内游标绑定工具、授权和前缀，外部客户端不接触底层令牌或节点地址。</summary>
internal sealed class McpCursorCache
{
    private sealed record Entry(string Tool, string GrantId, string Scope, object Data, long Created);
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    internal string Store(string tool, string grantId, string scope, object data)
    {
        lock (_entries)
        {
            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            if (_entries.Count >= 32) _entries.Remove(_entries.MinBy(item => item.Value.Created).Key);
            _entries.Add(id, new(tool, grantId, scope, data, Environment.TickCount64));
            return id;
        }
    }

    internal T? Read<T>(string cursor, string tool, string grantId, string scope) where T : class
    {
        if (cursor.Length == 0) return null;
        lock (_entries)
        {
            if (_entries.TryGetValue(cursor, out var entry) && entry.Tool == tool && entry.GrantId == grantId
                && entry.Scope == scope && Environment.TickCount64 - entry.Created <= (long)TimeSpan.FromMinutes(5).TotalMilliseconds
                && entry.Data is T state) return state;
            throw Text.Exception("McpInvalidCursor");
        }
    }
}
