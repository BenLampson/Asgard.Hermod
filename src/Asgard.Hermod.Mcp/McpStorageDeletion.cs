using System.Text;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;

namespace Asgard.Hermod.Mcp;

/// <summary>先冻结经过范围校验的对象键，再一次性提交删除；目录不会在执行时重新展开。</summary>
internal static class McpStorageDeletion
{
    private const int MaximumKeys = 1000;
    private const int MaximumPages = 100;
    private const int PageSize = 25;
    private const string PlanScope = "deletion";

    internal static async Task<object> PrepareAsync(IObjectStorageClient client, McpGrant grant, McpArguments args,
        McpCursorCache cursors, CancellationToken token)
    {
        string[] explicitKeys = args.Strings("keys");
        string[] prefixes = args.Strings("prefixes");
        if (explicitKeys.Length == 0 && prefixes.Length == 0) throw Text.Exception("McpStorageDeleteEmpty");
        foreach (string key in explicitKeys) ValidateKey(grant, key);
        foreach (string prefix in prefixes)
        {
            ValidateKey(grant, prefix);
            if (!prefix.EndsWith('/')) throw Text.Exception("McpStorageDeleteInvalidRange");
        }

        var keys = new HashSet<string>(explicitKeys, StringComparer.Ordinal);
        int pages = 0;
        foreach (string prefix in prefixes.Distinct(StringComparer.Ordinal))
        {
            // 部分连接器会省略与查询前缀相同的对象；仍需把所选目录占位键放入冻结清单。
            keys.Add(prefix);
            if (keys.Count > MaximumKeys) throw Text.Exception("McpStorageDeleteTooMany");
            string? next = null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                token.ThrowIfCancellationRequested();
                if (++pages > MaximumPages) throw Text.Exception("McpStorageDeleteTooMany");
                var page = await client.ListObjectsAsync(grant.Resource, prefix, next, null, MaximumKeys, token).ConfigureAwait(false);
                foreach (var item in page.Entries)
                {
                    ValidateKey(grant, item.Key);
                    if (!item.Key.StartsWith(prefix, StringComparison.Ordinal)) throw Text.Exception("McpStorageDeleteInvalidRange");
                    keys.Add(item.Key);
                    if (keys.Count > MaximumKeys) throw Text.Exception("McpStorageDeleteTooMany");
                }
                next = page.NextToken;
                if (next is not null && (next.Length is 0 or > 16384 || next.Any(char.IsControl) || !seen.Add(next)))
                    throw Text.Exception("McpStorageDeleteInvalidRange");
            } while (next is not null);
        }
        if (keys.Count == 0) throw Text.Exception("McpStorageDeleteEmpty");
        token.ThrowIfCancellationRequested();
        string[] snapshot = keys.Order(StringComparer.Ordinal).ToArray();
        string id = cursors.Store(McpToolCatalog.StoragePrepareDelete, grant.Id, PlanScope, snapshot);
        return Page(id, snapshot, 0);
    }

    internal static object Inspect(McpGrant grant, McpArguments args, McpCursorCache cursors)
    {
        string id = args.String("plan_id");
        var keys = cursors.Read<string[]>(id, McpToolCatalog.StoragePrepareDelete, grant.Id, PlanScope)
            ?? throw Text.Exception("McpInvalidCursor");
        return Page(id, keys, args.Integer("offset", 0));
    }

    internal static async Task<object> ExecuteAsync(IObjectStorageClient client, McpGrant grant, McpArguments args,
        McpCursorCache cursors, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string[] keys = cursors.Take<string[]>(args.String("plan_id"), McpToolCatalog.StoragePrepareDelete, grant.Id, PlanScope);
        try
        {
            // SDK 可能已提交请求或部分删除，即使之后发生超时或异常也不能安全重试。
            await client.DeleteAsync(grant.Resource, keys, token).ConfigureAwait(false);
        }
        catch (Exception) { throw Text.Exception("McpStorageDeleteOutcomeUnknown"); }
        return new { bucket = grant.Resource, submittedCount = keys.Length };
    }

    internal static void ValidateKey(McpGrant grant, string key)
    {
        if (string.IsNullOrEmpty(key) || Encoding.UTF8.GetByteCount(key) > 1023 || key.Any(char.IsControl)
            || !key.StartsWith(grant.Qualifier, StringComparison.Ordinal) || !McpGrantRules.IsObjectPath(key))
            throw Text.Exception("McpAccessDenied");
    }

    private static object Page(string id, string[] keys, int offset)
    {
        if (offset > keys.Length) throw Text.Exception("McpInvalidArguments");
        int end = Math.Min(offset + PageSize, keys.Length);
        return new { planId = id, total = keys.Length, keys = keys[offset..end], nextOffset = end < keys.Length ? (int?)end : null };
    }
}
