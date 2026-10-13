using System.IO.Enumeration;
using System.Text;

namespace Asgard.Hermod.ObjectStorage;

/// <summary>按已保存实例建立资源连接，共用文件传输、预览、目录与明确删除范围策略。</summary>
public sealed partial class LocalObjectStorageService : IObjectStorageService
{
    /// <summary>只有业务服务按实例标识恢复凭据，窗口不持有明文参数。</summary>
    private readonly IInstanceConfigurationService _configuration;
    /// <summary>服务商替换不影响 UI 或通用文件操作策略。</summary>
    private readonly IObjectStorageClientFactory _clients;

    /// <summary>应用服务通过接口依赖配置和资源连接器，不依赖具体云 SDK。</summary>
    public LocalObjectStorageService(IInstanceConfigurationService configuration, IObjectStorageClientFactory clients)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clients);
        _configuration = configuration;
        _clients = clients;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(string instanceId,
        CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, null,
            client => client.ListBucketsAsync(cancellationToken), cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string instanceId, ObjectStorageBucket bucket, string prefix,
        string search = "", string? continuationToken = null, CancellationToken cancellationToken = default) =>
        ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidatePrefix(prefix);
            string query = (search ?? "").Trim();
            ValidateKey(query, allowEmpty: true);
            bool wildcard = query.IndexOfAny(['*', '?']) >= 0;
            if (continuationToken?.Length > 16384 || continuationToken?.Any(char.IsControl) == true)
                throw ObjectStorageException.FromResource("InvalidObjectListCursorRefreshTheList", "InvalidContinuationToken");
            // OSS 仅支持前缀查询；逐页匹配直接子项名称，跳过无匹配页，避免把未加载对象误判为无结果。
            var seenTokens = new HashSet<string>(StringComparer.Ordinal);
            if (continuationToken is not null) seenTokens.Add(continuationToken);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectStoragePage page = await client.ListObjectsAsync(bucket.Name, prefix, continuationToken, "/", 100,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (page.Entries.Any(entry => !entry.Key.StartsWith(prefix, StringComparison.Ordinal)))
                    throw ObjectStorageException.FromResource("TheObjectListReturnedAnInvalidRangeRefreshAndTryAgain", "InvalidResponse");
                if (page.NextToken is { } next && (next.Length == 0 || next.Length > 16384
                    || next.Any(char.IsControl) || !seenTokens.Add(next)))
                    throw ObjectStorageException.FromResource("InvalidObjectListCursorRefreshTheList", "InvalidResponse");
                var entries = page.Entries.Where(entry => entry.Key != prefix
                    && MatchesSearch(entry.Key[prefix.Length..].TrimEnd('/'), query, wildcard)).ToArray();
                if (query.Length == 0 || entries.Length > 0 || page.NextToken is null)
                    return page with { Entries = entries };
                continuationToken = page.NextToken;
            }
        }, cancellationToken);

    /// <summary>普通词按包含匹配；含星号或问号时按完整名称通配匹配，其他符号保持字面意义。</summary>
    private static bool MatchesSearch(string name, string query, bool wildcard) => wildcard
        ? FileSystemName.MatchesSimpleExpression(query, name, ignoreCase: true)
        : name.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectDetailsAsync(string instanceId, ObjectStorageBucket bucket,
        string key, CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidateKey(key);
            ObjectStorageObjectDetails details = await client.GetObjectMetadataAsync(bucket.Name, key,
                cancellationToken).ConfigureAwait(false);
            return await ObjectStorageTextPreview.ReadAsync(client, bucket.Name, details, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string instanceId, ObjectStorageBucket bucket, string key,
        CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, bucket, client =>
        {
            ValidateKey(key);
            return client.ObjectExistsAsync(bucket.Name, key, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc />
    public Task UploadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        bool overwrite, IProgress<ObjectStorageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidateKey(key);
            ValidateLocalPath(localPath);
            await using var file = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await client.UploadAsync(bucket.Name, key, file, file.Length, overwrite, progress,
                cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public Task DownloadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        IProgress<ObjectStorageTransferProgress>? progress = null, CancellationToken cancellationToken = default,
        long? maxBytes = null) =>
        ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidateKey(key);
            ValidateLocalPath(localPath);
            if (maxBytes is <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            string destination = Path.GetFullPath(localPath);
            string directory = Path.GetDirectoryName(destination)!;
            EnsureDownloadDirectory(directory, createMissing: true);
            // 同一目录内原子替换，传输失败和取消不会损坏已有文件。
            string temporary = Path.Combine(directory, $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.download");
            try
            {
                long transferred = 0;
                long totalBytes;
                await using (ObjectStorageReadResult remote = await client.OpenReadAsync(bucket.Name, key, null,
                    cancellationToken).ConfigureAwait(false))
                {
                    totalBytes = remote.Size;
                    if (maxBytes is { } limit && remote.Size > limit)
                        throw ObjectStorageException.FromResource("FilePreviewTooLarge", "PreviewLimitExceeded");
                    await using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    ObjectStorageProgress.Report(progress, 0, remote.Size);
                    byte[] buffer = new byte[64 * 1024];
                    while (true)
                    {
                        int read = await remote.Body.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                        if (read == 0) break;
                        if (maxBytes is { } bodyLimit && transferred > bodyLimit - read)
                            throw ObjectStorageException.FromResource("FilePreviewTooLarge", "PreviewLimitExceeded");
                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                        transferred += read;
                        ObjectStorageProgress.Report(progress, transferred, remote.Size);
                    }
                    if (remote.Size >= 0 && transferred != remote.Size)
                        throw ObjectStorageException.FromResource("TheDownloadIsIncompleteCheckYourNetworkAndTryAgain", "IncompleteDownload");
                    await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
                EnsureDownloadDirectory(directory, createMissing: false);
                File.Move(temporary, destination, overwrite: true);
                ObjectStorageProgress.Report(progress, transferred, totalBytes);
                return true;
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task CreateFolderAsync(string instanceId, ObjectStorageBucket bucket, string prefix, string name,
        CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, bucket, async client =>
        {
            ValidatePrefix(prefix);
            string folderName = (name ?? "").Trim();
            if (folderName.Length == 0 || folderName is "." or ".." ||
                folderName.Any(value => char.IsControl(value) || value is '/' or '\\'))
                throw ObjectStorageException.FromResource("DirectoryNamesCannotBeEmptyOrContainSlashesOrControlCharacters", "InvalidFolderName");
            string fileKey = prefix + folderName;
            string folderKey = fileKey + "/";
            ValidateKey(folderKey);
            if (await client.ObjectExistsAsync(bucket.Name, fileKey, cancellationToken).ConfigureAwait(false) ||
                (await client.ListObjectsAsync(bucket.Name, folderKey, null, null, 1, cancellationToken).ConfigureAwait(false)).Entries.Count > 0)
                throw ObjectStorageException.FromResource("AFileOrFolderWithThisNameAlreadyExistsChooseADifferentFolderName", "FileAlreadyExists");
            using var empty = new MemoryStream();
            await client.UploadAsync(bucket.Name, folderKey, empty, 0, overwrite: false, progress: null,
                cancellationToken).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string instanceId, ObjectStorageBucket bucket,
        IReadOnlyList<ObjectStorageEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ObjectStorageEntry[] selected = entries.ToArray();
        return ExecuteAsync(instanceId, bucket, async client =>
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (ObjectStorageEntry entry in selected)
            {
                ValidateKey(entry.Key);
                keys.Add(entry.Key);
                if (!entry.IsDirectory) continue;
                if (!entry.Key.EndsWith('/'))
                    throw ObjectStorageException.FromResource("InvalidDirectoryDeletionRangeRefreshAndSelectAgain", "InvalidDeleteRange");
                string? token = null;
                var seenTokens = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ObjectStoragePage page = await client.ListObjectsAsync(bucket.Name, entry.Key, token, null, 100,
                        cancellationToken).ConfigureAwait(false);
                    foreach (ObjectStorageEntry item in page.Entries)
                    {
                        ValidateKey(item.Key);
                        if (!item.Key.StartsWith(entry.Key, StringComparison.Ordinal))
                            throw ObjectStorageException.FromResource("DirectoryEnumerationReturnedAnIncompleteDeletionRangeRefreshAndTryAgain", "InvalidDeleteRange");
                        keys.Add(item.Key);
                    }
                    token = page.NextToken;
                    if (token is not null && !seenTokens.Add(token))
                        throw ObjectStorageException.FromResource("InvalidDirectoryPagingCursorRefreshAndTryAgain", "InvalidResponse");
                } while (token is not null);
            }
            return new ObjectStorageDeletePlan(Array.AsReadOnly(keys.Order(StringComparer.Ordinal).ToArray()));
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(string instanceId, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(plan.Keys);
        string[] keys = plan.Keys.Distinct(StringComparer.Ordinal).ToArray();
        return ExecuteAsync(instanceId, bucket, async client =>
        {
            foreach (string key in keys) ValidateKey(key);
            int completedBatches = 0;
            try
            {
                foreach (string[] batch in keys.Chunk(1000))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await client.DeleteAsync(bucket.Name, batch, cancellationToken).ConfigureAwait(false);
                    completedBatches++;
                }
            }
            catch (ObjectStorageException) when (completedBatches > 0)
            {
                throw ObjectStorageException.FromResource("SomeObjectsWereDeletedButTheRemainingObjectsCouldNotBeDeletedRefreshAndConfirmAgain", "PartialDelete");
            }
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string> CreateTemporaryLinkAsync(string instanceId, ObjectStorageBucket bucket, string key,
        TimeSpan lifetime, CancellationToken cancellationToken = default) => ExecuteAsync(instanceId, bucket, client =>
        {
            ValidateKey(key);
            if (lifetime != TimeSpan.FromMinutes(15) && lifetime != TimeSpan.FromMinutes(60))
                throw ObjectStorageException.FromResource("TemporaryLinksCanLastOnly15Or60Minutes", "InvalidLinkLifetime");
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(client.CreateTemporaryLink(bucket.Name, key, lifetime));
        }, cancellationToken);

    /// <summary>所有资源能力统一从已保存实例恢复配置，不把第三方异常直接返回给视图。</summary>
    private async Task<T> ExecuteAsync<T>(string instanceId, ObjectStorageBucket? bucket,
        Func<IObjectStorageClient, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IObjectStorageClient? client = null;
        try
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                throw ObjectStorageException.FromResource("SelectAConfiguredInstanceFirst", "InstanceNotFound");
            if (bucket is not null && (string.IsNullOrWhiteSpace(bucket.Name) || bucket.Name.Any(char.IsControl)))
                throw ObjectStorageException.FromResource("InvalidBucketNameSelectTheBucketAgain", "InvalidBucket");
            InstanceConnectionOptions? options = await _configuration.GetConnectionAsync(instanceId,
                cancellationToken).ConfigureAwait(false);
            if (options is null) throw ObjectStorageException.FromResource("TheInstanceConfigurationNoLongerExistsConfigureTheInstanceAgain", "InstanceNotFound");
            client = _clients.CreateClient(options, bucket?.Region);
            return await operation(client).ConfigureAwait(false);
        }
        catch (ObjectStorageException) { throw; }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw ObjectStorageException.FromResource("ObjectStorageOperationFailedCheckTheNetworkInstanceConfigurationAndLocalFilePermissio", "StorageOperationFailed");
        }
        finally
        {
            // 客户端释放属于清理阶段，不能把已提交的上传、下载或删除反馈为操作失败。
            try { client?.Dispose(); }
            catch (Exception) { }
        }
    }

    /// <summary>对象键作为数据传递，不参与本地文件路径计算或错误消息拼接。</summary>
    private static void ValidateKey(string key, bool allowEmpty = false)
    {
        if (key is null || (!allowEmpty && key.Length == 0) || Encoding.UTF8.GetByteCount(key) > 1023 || key.Any(char.IsControl))
            throw ObjectStorageException.FromResource("InvalidObjectNameRefreshTheListOrChangeTheName", "InvalidObjectKey");
    }

    /// <summary>浏览和创建目录的前缀只能为空或完整目录，不允许隐式扩展到邻接名称。</summary>
    private static void ValidatePrefix(string prefix)
    {
        ValidateKey(prefix, allowEmpty: true);
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
            throw ObjectStorageException.FromResource("InvalidDirectoryPathSelectTheDirectoryAgain", "InvalidPrefix");
    }

    /// <summary>本地路径完全来自用户文件选择，异常路径不传入 SDK 或错误文案。</summary>
    private static void ValidateLocalPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw ObjectStorageException.FromResource("SelectAValidLocalFileLocation", "InvalidLocalPath");
    }

    /// <summary>映射后的子目录由后台创建，拒绝沿目录联接或符号链接写出所选位置。</summary>
    private static void EnsureDownloadDirectory(string path, bool createMissing)
    {
        string fullPath = Path.GetFullPath(path);
        string current = Path.GetPathRoot(fullPath)!;
        foreach (string segment in fullPath[current.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (!Directory.Exists(current))
            {
                if (!createMissing)
                    throw ObjectStorageException.FromResource("TheDownloadDirectoryHasChangedSelectTheDestinationAgain", "InvalidLocalPath");
                Directory.CreateDirectory(current);
            }
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw ObjectStorageException.FromResource("TheDownloadDirectoryContainsAJunctionOrSymbolicLinkSelectARegularDirectory", "InvalidLocalPath");
        }
    }
}
