using System.Text;
using Asgard.Hermod.ObjectStorage.Abstractions.Clients;

namespace Asgard.Hermod.Mcp;

/// <summary>复用通用对象存储客户端，只允许已授权 Bucket 前缀内的读取。</summary>
internal sealed class McpStorageTools(IObjectStorageClientFactory factory)
{
    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args,
        McpCursorCache cursors, CancellationToken token)
    {
        bool listing = name == "hermod_storage_list";
        string target = listing ? args.String("prefix", grant.Qualifier) : args.String("key");
        if (!target.StartsWith(grant.Qualifier, StringComparison.Ordinal) || !McpGrantRules.IsObjectPath(target)) throw Text.Exception("McpAccessDenied");
        using var client = factory.CreateClient(connection);
        if (listing)
        {
            string? continuation = cursors.Read<string>(args.String("cursor"), name, grant.Id, target);
            var page = await client.ListObjectsAsync(grant.Resource, target, continuation, null, 30, token).ConfigureAwait(false);
            var entries = page.Entries.Take(30).Where(entry => entry.Key.StartsWith(target, StringComparison.Ordinal))
                .Select(entry => new { entry.Key, entry.Size, entry.LastModified, entry.IsDirectory }).ToArray();
            string? cursor = page.NextToken is { Length: > 0 and <= 8192 } next ? cursors.Store(name, grant.Id, target, next) : null;
            return new { bucket = grant.Resource, prefix = target, entries, nextCursor = cursor, hasMore = page.NextToken is { Length: > 0 } };
        }
        if (name == "hermod_storage_stat")
        {
            if (!await client.ObjectExistsAsync(grant.Resource, target, token).ConfigureAwait(false))
                return new { bucket = grant.Resource, key = target, exists = false };
            var item = await client.GetObjectMetadataAsync(grant.Resource, target, token).ConfigureAwait(false);
            return new { bucket = grant.Resource, key = target, exists = true, item.Size, item.LastModified, item.StorageClass, item.ContentType };
        }

        const int maximum = 16 * 1024;
        await using var body = await client.OpenReadAsync(grant.Resource, target, maximum + 1, token).ConfigureAwait(false);
        byte[] bytes = new byte[maximum + 1];
        int count = await body.Body.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, token).ConfigureAwait(false);
        bool truncated = count > maximum || body.Size > count;
        string text;
        try
        {
            // 区间可能切在 UTF-8 字符中间，只有确知还有内容时才允许忽略结尾的半个字符。
            var decoder = new UTF8Encoding(false, true).GetDecoder();
            char[] chars = new char[maximum];
            int characters = decoder.GetChars(bytes.AsSpan(0, Math.Min(maximum, count)), chars, flush: !truncated);
            text = new string(chars, 0, characters);
        }
        catch (DecoderFallbackException) { throw Text.Exception("McpStorageNotText"); }
        if (text.Any(ch => char.IsControl(ch) && ch is not ('\t' or '\n' or '\r'))) throw Text.Exception("McpStorageNotText");
        return new { bucket = grant.Resource, key = target, content = text, truncated };
    }
}
