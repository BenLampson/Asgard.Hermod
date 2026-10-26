namespace Asgard.Hermod.ObjectStorage;

public sealed partial class LocalObjectStorageService
{
    /// <inheritdoc />
    public Task<ObjectStorageFolderStatistics> GetFolderStatisticsAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, IProgress<ObjectStorageFolderStatistics>? progress = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidatePrefix(prefix);
            // 平铺列举只读元数据；仅保留目录集合用于去重，不下载正文或逐文件 HEAD。
            var folders = new HashSet<string>(StringComparer.Ordinal);
            var tokens = new HashSet<string>(StringComparer.Ordinal);
            string? token = null;
            long files = 0, bytes = 0;
            DateTimeOffset? modified = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await client.ListObjectsAsync(bucket.Name, prefix, token, null, 1000, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var entry in page.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateKey(entry.Key);
                    if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal) || entry.Size < 0)
                        throw ObjectStorageException.FromResource("TheObjectListReturnedAnInvalidRangeRefreshAndTryAgain", "InvalidResponse");
                    if (entry.Key == prefix) continue;
                    bool directory = entry.IsDirectory || entry.Key.EndsWith('/');
                    if (directory && !entry.Key.EndsWith('/'))
                        throw ObjectStorageException.FromResource("TheObjectListReturnedAnInvalidRangeRefreshAndTryAgain", "InvalidResponse");
                    for (int slash = entry.Key.IndexOf('/', prefix.Length); slash >= 0; slash = entry.Key.IndexOf('/', slash + 1))
                        folders.Add(entry.Key[..(slash + 1)]);
                    if (directory) continue;
                    checked { files++; bytes += entry.Size; }
                    if (entry.LastModified is { } date && (modified is null || date > modified)) modified = date;
                }
                token = page.NextToken;
                if (token is not null && (token.Length == 0 || token.Length > 16384 || token.Any(char.IsControl) || !tokens.Add(token)))
                    throw ObjectStorageException.FromResource("InvalidDirectoryPagingCursorRefreshAndTryAgain", "InvalidResponse");
                progress?.Report(new(files, folders.Count, bytes, modified));
            } while (token is not null);
            cancellationToken.ThrowIfCancellationRequested();
            return new ObjectStorageFolderStatistics(files, folders.Count, bytes, modified);
        }, cancellationToken);
}
