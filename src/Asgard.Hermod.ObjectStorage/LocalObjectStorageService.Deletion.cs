namespace Asgard.Hermod.ObjectStorage;

public sealed partial class LocalObjectStorageService
{
    /// <inheritdoc />
    public Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string instanceId, ObjectStorageBucket bucket,
        IReadOnlyList<ObjectStorageEntry> entries, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ObjectStorageEntry[] selected = entries.ToArray();
        return ExecuteAsync(instanceId, bucket, async client =>
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var directories = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in selected)
            {
                ValidateKey(entry.Key);
                // 即使服务未返回选中目录本身，也对目录占位键执行幂等删除。
                keys.Add(entry.Key);
                if (!entry.IsDirectory) continue;
                if (!entry.Key.EndsWith('/'))
                    throw ObjectStorageException.FromResource("InvalidDirectoryDeletionRangeRefreshAndSelectAgain", "InvalidDeleteRange");
                directories.Add(entry.Key);
            }
            ObjectStorageProgress.Report(progress, keys.Count);
            foreach (string prefix in directories)
            {
                string? cursor = null;
                var seenCursors = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    // 不设置分隔符，一页可获取整个前缀下最多 1,000 个对象，包含后代目录。
                    var page = await client.ListObjectsAsync(bucket.Name, prefix, cursor, null, 1000, cancellationToken).ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    foreach (var item in page.Entries)
                    {
                        ValidateKey(item.Key);
                        if (!item.Key.StartsWith(prefix, StringComparison.Ordinal))
                            throw ObjectStorageException.FromResource("DirectoryEnumerationReturnedAnIncompleteDeletionRangeRefreshAndTryAgain", "InvalidDeleteRange");
                        keys.Add(item.Key);
                    }
                    cursor = page.NextToken;
                    if (cursor is not null && (cursor.Length == 0 || cursor.Length > 16384
                        || cursor.Any(char.IsControl) || !seenCursors.Add(cursor)))
                        throw ObjectStorageException.FromResource("InvalidDirectoryPagingCursorRefreshAndTryAgain", "InvalidResponse");
                    ObjectStorageProgress.Report(progress, keys.Count);
                } while (cursor is not null);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new ObjectStorageDeletePlan(Array.AsReadOnly(keys.Order(StringComparer.Ordinal).ToArray()));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string instanceId, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Keys);
        string[] keys = plan.Keys.Distinct(StringComparer.Ordinal).ToArray();
        return ExecuteAsync(instanceId, bucket, async client =>
        {
            foreach (string key in keys) ValidateKey(key);
            int completed = 0;
            try
            {
                foreach (string[] batch in keys.Chunk(1000))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await client.DeleteAsync(bucket.Name, batch, cancellationToken).ConfigureAwait(false);
                    completed += batch.Length;
                    ObjectStorageProgress.Report(progress, completed);
                }
            }
            catch (ObjectStorageException) when (completed > 0)
            {
                throw ObjectStorageException.FromResource("SomeObjectsWereDeletedButTheRemainingObjectsCouldNotBeDeletedRefreshAndConfirmAgain", "PartialDelete");
            }
            return true;
        }, cancellationToken);
    }
}
