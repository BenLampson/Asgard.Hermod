using System.Text;
using Asgard.Hermod.ObjectStorage.Abstractions.Services;

namespace Asgard.Hermod.Mcp;

/// <summary>复用通用对象存储客户端，所有操作均限制在明确授权的 Bucket 和前缀内。</summary>
internal sealed class McpStorageTools(McpStorageClients clients)
{
    internal async Task<object> RunAsync(string name, McpGrant grant, InstanceConnectionOptions connection, McpArguments args,
        McpCursorCache cursors, CancellationToken token, Func<string, bool>? allowed = null)
    {
        if (name == McpToolCatalog.StorageDeletePlan) return McpStorageDeletion.Inspect(grant, args, cursors);
        if (name == McpToolCatalog.StorageStatistics)
        {
            string prefix = args.String("prefix", grant.Qualifier);
            if (!prefix.StartsWith(grant.Qualifier, StringComparison.Ordinal) || !McpGrantRules.IsObjectPath(prefix)
                || prefix.Length > 0 && (!prefix.EndsWith('/') || Encoding.UTF8.GetByteCount(prefix) > 1023))
                throw Text.Exception("McpAccessDenied");
            using var statisticsClient = await clients.CreateAsync(grant, connection, token).ConfigureAwait(false);
            return await McpStorageStatistics.RunAsync(statisticsClient, grant, prefix, token).ConfigureAwait(false);
        }
        if (name == McpToolCatalog.StoragePrepareDelete || name == McpToolCatalog.StorageExecuteDelete)
        {
            using var deleteClient = await clients.CreateAsync(grant, connection, token).ConfigureAwait(false);
            return name == McpToolCatalog.StoragePrepareDelete
                ? await McpStorageDeletion.PrepareAsync(deleteClient, grant, args, cursors, token).ConfigureAwait(false)
                : await McpStorageDeletion.ExecuteAsync(deleteClient, grant, args, cursors, token).ConfigureAwait(false);
        }
        bool listing = name == "hermod_storage_list";
        string target = listing ? args.String("prefix", grant.Qualifier) : args.String("key");
        if (!target.StartsWith(grant.Qualifier, StringComparison.Ordinal) || !McpGrantRules.IsObjectPath(target)) throw Text.Exception("McpAccessDenied");
        using var client = await clients.CreateAsync(grant, connection, token).ConfigureAwait(false);
        if (name == McpToolCatalog.StorageCreateFolder)
            return await CreateFolderAsync(client, grant, target, token).ConfigureAwait(false);
        if (name == McpToolCatalog.StorageSignedLink)
        {
            McpStorageDeletion.ValidateKey(grant, target);
            int minutes = args.Integer("lifetime_minutes", 0);
            if (minutes is not (15 or 60)) throw Text.Exception("McpInvalidArguments");
            token.ThrowIfCancellationRequested();
            return new
            {
                bucket = grant.Resource,
                key = target,
                lifetimeMinutes = minutes,
                url = client.CreateTemporaryLink(grant.Resource, target, TimeSpan.FromMinutes(minutes))
            };
        }
        if (listing)
        {
            string? continuation = cursors.Read<string>(args.String("cursor"), name, grant.Id, target);
            var page = await client.ListObjectsAsync(grant.Resource, target, continuation, null, 30, token).ConfigureAwait(false);
            var entries = page.Entries.Take(30).Where(entry => entry.Key.StartsWith(target, StringComparison.Ordinal) && (allowed is null || allowed(entry.Key)))
                .Select(entry => new { entry.Key, entry.Size, entry.LastModified, entry.IsDirectory }).ToArray();
            string? cursor = page.NextToken is { Length: > 0 and <= 8192 } next ? cursors.Store(name, grant.Id, target, next) : null;
            return new { bucket = grant.Resource, prefix = target, entries, nextCursor = cursor, hasMore = page.NextToken is { Length: > 0 } };
        }
        if (name == McpToolCatalog.StorageStat)
        {
            if (!await client.ObjectExistsAsync(grant.Resource, target, token).ConfigureAwait(false))
                return new { bucket = grant.Resource, key = target, exists = false };
            var item = await client.GetObjectMetadataAsync(grant.Resource, target, token).ConfigureAwait(false);
            return new { bucket = grant.Resource, key = target, exists = true, item.Size, item.LastModified, item.StorageClass, item.ContentType, item.AccessControl };
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

    private static async Task<object> CreateFolderAsync(Asgard.Hermod.ObjectStorage.Abstractions.Clients.IObjectStorageClient client,
        McpGrant grant, string key, CancellationToken token)
    {
        McpStorageDeletion.ValidateKey(grant, key);
        if (!key.EndsWith('/') || key.Length == 1 || key[^2] == '/') throw Text.Exception("McpStorageInvalidFolder");
        string fileKey = key[..^1];
        if (fileKey.StartsWith(grant.Qualifier, StringComparison.Ordinal)
            && await client.ObjectExistsAsync(grant.Resource, fileKey, token).ConfigureAwait(false))
            throw Text.Exception("McpStorageFolderExists");
        var page = await client.ListObjectsAsync(grant.Resource, key, null, null, 1, token).ConfigureAwait(false);
        if (page.Entries.Any(item => !item.Key.StartsWith(key, StringComparison.Ordinal)) || page.NextToken is not null && page.Entries.Count == 0)
            throw Text.Exception("McpStorageDeleteInvalidRange");
        if (page.Entries.Count > 0) throw Text.Exception("McpStorageFolderExists");
        token.ThrowIfCancellationRequested();
        using var empty = new MemoryStream();
        try { await client.UploadAsync(grant.Resource, key, empty, 0, overwrite: false, progress: null, token).ConfigureAwait(false); }
        catch (ObjectStorageException error) when (error.ErrorCode == "FileAlreadyExists") { throw Text.Exception("McpStorageFolderExists"); }
        catch (Exception) { throw Text.Exception("McpStorageCreateOutcomeUnknown"); }
        return new { bucket = grant.Resource, key, created = true };
    }
}
