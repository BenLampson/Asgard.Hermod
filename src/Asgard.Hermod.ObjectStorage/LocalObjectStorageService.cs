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
            await ObjectStorageFileTransfer.UploadAsync(client, bucket.Name, key, localPath, overwrite, progress,
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
            await ObjectStorageFileTransfer.DownloadAsync(client, bucket.Name, key, localPath, overwrite: true, progress,
                cancellationToken, maxBytes).ConfigureAwait(false);
            return true;
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

}
