using Asgard.Hermod.ObjectStorage.Abstractions.Clients;

namespace Asgard.Hermod.Mcp;

/// <summary>递归汇总授权前缀内的对象元数据，不下载正文或返回未完成的统计。</summary>
internal static class McpStorageStatistics
{
    private const int MaximumEntries = 100000;
    private const int MaximumPages = 100;

    internal static async Task<object> RunAsync(IObjectStorageClient client, McpGrant grant, string prefix, CancellationToken token)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        long files = 0, bytes = 0;
        int entries = 0, pages = 0;
        DateTimeOffset? modified = null;
        do
        {
            token.ThrowIfCancellationRequested();
            if (++pages > MaximumPages) throw Text.Exception("McpStorageStatisticsLimit");
            var page = await client.ListObjectsAsync(grant.Resource, prefix, cursor, null, 1000, token).ConfigureAwait(false);
            foreach (var item in page.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (++entries > MaximumEntries) throw Text.Exception("McpStorageStatisticsLimit");
                McpStorageDeletion.ValidateKey(grant, item.Key);
                if (!item.Key.StartsWith(prefix, StringComparison.Ordinal) || item.Size < 0)
                    throw Text.Exception("McpStorageDeleteInvalidRange");
                if (item.Key == prefix) continue;
                bool directory = item.IsDirectory || item.Key.EndsWith('/');
                if (directory && !item.Key.EndsWith('/')) throw Text.Exception("McpStorageDeleteInvalidRange");
                for (int slash = item.Key.IndexOf('/', prefix.Length); slash >= 0; slash = item.Key.IndexOf('/', slash + 1))
                    if (folders.Add(item.Key[..(slash + 1)]) && folders.Count > MaximumEntries)
                        throw Text.Exception("McpStorageStatisticsLimit");
                if (directory) continue;
                checked { files++; bytes += item.Size; }
                if (item.LastModified is { } date && (modified is null || date > modified)) modified = date;
            }
            cursor = page.NextToken;
            if (cursor is not null && (cursor.Length is 0 or > 16384 || cursor.Any(char.IsControl) || !seen.Add(cursor)))
                throw Text.Exception("McpStorageDeleteInvalidRange");
        } while (cursor is not null);
        token.ThrowIfCancellationRequested();
        return new { bucket = grant.Resource, prefix, files, folders = folders.Count, bytes, lastModified = modified };
    }
}
