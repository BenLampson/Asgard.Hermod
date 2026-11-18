using System.Globalization;
using System.Text;
using Asgard.Hermod.Redis;

namespace Asgard.Hermod.Mcp;

/// <summary>用 Redis 字面前缀扫描和逐键元数据查询；不获取值、节点地址或全库统计。</summary>
internal sealed class McpRedisTools(IRedisSessionFactory factory)
{
    private sealed record Position(string Node, string Cursor, string[] Remaining, bool Truncated);

    internal async Task<object> ListDatabasesAsync(InstanceConnectionOptions connection, Func<string, bool> allowed, CancellationToken token)
    {
        await using var session = await factory.OpenAsync(connection, token).ConfigureAwait(false);
        var databases = Enumerable.Range(0, Math.Clamp(session.DatabaseCount, 0, 256))
            .Where(index => allowed(index.ToString(CultureInfo.InvariantCulture))).ToArray();
        return new { kind = "databases", items = databases, truncated = session.DatabaseCount > 256 };
    }

    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args,
        McpCursorCache cursors, CancellationToken token, Func<string, bool>? allowed = null)
    {
        bool scanning = name == "hermod_redis_scan_keys";
        string target = scanning ? args.String("prefix", grant.Qualifier) : args.String("key");
        if (!target.StartsWith(grant.Qualifier, StringComparison.Ordinal)) throw Text.Exception("McpAccessDenied");
        int db = int.Parse(grant.Resource, CultureInfo.InvariantCulture);
        await using var session = await factory.OpenAsync(connection, token).ConfigureAwait(false);
        if (db >= session.DatabaseCount) throw Text.Exception("McpInvalidGrant");
        if (name == McpToolCatalog.RedisReadValue)
            return await McpRedisValueReader.ReadAsync(session, db, target, grant.Id, args.String("cursor"), cursors, token).ConfigureAwait(false);
        if (!scanning)
        {
            var metadata = await session.ReadKeyMetadataAsync(db, RedisKeyId.FromText(target), token).ConfigureAwait(false);
            return new { database = db, key = target, metadata.Type, metadata.TtlMilliseconds };
        }

        var nodes = session.ScanNodes.Order(StringComparer.Ordinal).ToArray();
        var position = cursors.Read<Position>(args.String("cursor"), name, grant.Id, target);
        if (nodes.Length == 0) throw Text.Exception("McpAccessUnavailable");
        int nodeIndex = position is null ? 0 : Array.IndexOf(nodes, position.Node);
        if (nodeIndex < 0) throw Text.Exception("McpInvalidCursor");
        string node = nodes[nodeIndex];
        string cursor = position?.Cursor ?? "0";
        string[]? keys = position?.Remaining;
        bool truncated = position?.Truncated ?? false;
        if (keys is not { Length: > 0 })
        {
            var page = await session.ScanAsync(node, db, cursor, Pattern(target), "", token).ConfigureAwait(false);
            var matched = page.Keys.Where(key => key.IsText).Select(key => key.DisplayName)
                .Where(key => key.Length <= 1024 && key.StartsWith(target, StringComparison.Ordinal) && (allowed is null || allowed(key))).Distinct(StringComparer.Ordinal).Take(201).ToArray();
            truncated |= matched.Length > 200;
            keys = matched.Take(200).ToArray();
            cursor = page.Cursor;
        }
        var output = keys.Take(15).ToArray();
        var remaining = keys.Skip(output.Length).ToArray();
        string? next = null;
        if (remaining.Length > 0 || cursor != "0")
            next = cursors.Store(name, grant.Id, target, new Position(node, cursor, remaining, truncated));
        else if (nodeIndex + 1 < nodes.Length)
            next = cursors.Store(name, grant.Id, target, new Position(nodes[nodeIndex + 1], "0", [], truncated));
        return new { database = db, prefix = target, keys = output, nextCursor = next, truncated };
    }

    internal static string Pattern(string prefix)
    {
        var pattern = new StringBuilder(prefix.Length + 1);
        foreach (char value in prefix)
        {
            if (value is '*' or '?' or '[' or ']' or '\\') pattern.Append('\\');
            pattern.Append(value);
        }
        return pattern.Append('*').ToString();
    }
}
