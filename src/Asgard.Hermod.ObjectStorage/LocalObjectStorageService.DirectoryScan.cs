namespace Asgard.Hermod.ObjectStorage;

public sealed partial class LocalObjectStorageService
{
    /// <summary>超过此文件数量后暂停，用户确认后继续本次扫描。</summary>
    public const int DirectoryScanConfirmationThreshold = 10_000;

    /// <inheritdoc />
    public Task<ObjectStorageDirectoryScanResult?> ScanDirectoryAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, Func<ObjectStorageDirectoryScanProgress, CancellationToken, Task<bool>> confirmContinuation,
        IProgress<ObjectStorageDirectoryScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmContinuation);
        return ExecuteAsync<ObjectStorageDirectoryScanResult?>(instanceId, bucket, async client =>
        {
            ValidatePrefix(prefix);
            // 只保留当前列表所需的直接子项，不保存全部后代记录，不创建缓存或索引。
            var children = new Dictionary<string, ObjectStorageEntry>(StringComparer.Ordinal);
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            string? token = null;
            long files = 0, pages = 0;
            bool confirmed = false;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectStoragePage page = await client.ListObjectsAsync(bucket.Name, prefix, token, null, 1000,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                foreach (ObjectStorageEntry entry in page.Entries)
                {
                    ValidateKey(entry.Key);
                    if (!entry.Key.StartsWith(prefix, StringComparison.Ordinal))
                        throw ObjectStorageException.FromResource("TheObjectListReturnedAnInvalidRangeRefreshAndTryAgain", "InvalidResponse");
                    if (entry.Key == prefix) continue;
                    bool directory = entry.IsDirectory || entry.Key.EndsWith('/');
                    if (!directory) files++;
                    string relative = entry.Key[prefix.Length..];
                    int slash = relative.IndexOf('/');
                    if (slash < 0 && !directory) { children[entry.Key] = entry; continue; }
                    if (slash < 0)
                        throw ObjectStorageException.FromResource("TheObjectListReturnedAnInvalidRangeRefreshAndTryAgain", "InvalidResponse");
                    string key = prefix + relative[..(slash + 1)];
                    DateTimeOffset? modified = directory ? null : entry.LastModified;
                    if (!children.TryGetValue(key, out ObjectStorageEntry? folder)) children[key] = new(key, true, LastModified: modified);
                    else if (modified is not null && (folder.LastModified is null || modified > folder.LastModified))
                        children[key] = folder with { LastModified = modified };
                }
                token = page.NextToken;
                if (token is not null && (token.Length == 0 || token.Length > 16384 || token.Any(char.IsControl) || !seenTokens.Add(token)))
                    throw ObjectStorageException.FromResource("InvalidDirectoryPagingCursorRefreshAndTryAgain", "InvalidResponse");
                var state = new ObjectStorageDirectoryScanProgress(files, ++pages);
                progress?.Report(state);
                if (!confirmed && files > DirectoryScanConfirmationThreshold)
                {
                    if (!await confirmContinuation(state, cancellationToken).ConfigureAwait(false)) return null;
                    cancellationToken.ThrowIfCancellationRequested();
                    confirmed = true;
                }
            } while (token is not null);
            cancellationToken.ThrowIfCancellationRequested();
            return new ObjectStorageDirectoryScanResult(Array.AsReadOnly(children.Values
                .OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.LastModified)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray()), files);
        }, cancellationToken);
    }
}
