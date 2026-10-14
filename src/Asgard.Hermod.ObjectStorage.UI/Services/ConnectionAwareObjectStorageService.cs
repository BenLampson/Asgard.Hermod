namespace Asgard.Hermod.ObjectStorage.UI.Services;

/// <summary>在统一服务边界报告认证和网络失效，覆盖列表、详情、扫描和传输，不拦截普通对象错误。</summary>
internal sealed class ConnectionAwareObjectStorageService(IObjectStorageService inner, Action<Exception> failed) : IObjectStorageService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(string id, CancellationToken token = default) => ObserveAsync(() => inner.ListBucketsAsync(id, token), token);
    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string id, ObjectStorageBucket bucket, string prefix, string search = "", string? continuationToken = null, CancellationToken cancellationToken = default) =>
        ObserveAsync(() => inner.ListObjectsAsync(id, bucket, prefix, search, continuationToken, cancellationToken), cancellationToken);
    /// <inheritdoc />
    public Task<ObjectStorageDirectoryScanResult?> ScanDirectoryAsync(string id, ObjectStorageBucket bucket, string prefix, Func<ObjectStorageDirectoryScanProgress, CancellationToken, Task<bool>> confirmContinuation,
        IProgress<ObjectStorageDirectoryScanProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ObserveAsync(() => inner.ScanDirectoryAsync(id, bucket, prefix, confirmContinuation, progress, cancellationToken), cancellationToken);
    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectDetailsAsync(string id, ObjectStorageBucket bucket, string key, CancellationToken token = default) =>
        ObserveAsync(() => inner.GetObjectDetailsAsync(id, bucket, key, token), token);
    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string id, ObjectStorageBucket bucket, string key, CancellationToken token = default) =>
        ObserveAsync(() => inner.ObjectExistsAsync(id, bucket, key, token), token);
    /// <inheritdoc />
    public Task UploadAsync(string id, ObjectStorageBucket bucket, string key, string localPath, bool overwrite, IProgress<ObjectStorageTransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
        ObserveAsync(() => inner.UploadAsync(id, bucket, key, localPath, overwrite, progress, cancellationToken), cancellationToken);
    /// <inheritdoc />
    public Task DownloadAsync(string id, ObjectStorageBucket bucket, string key, string localPath, IProgress<ObjectStorageTransferProgress>? progress = null, CancellationToken cancellationToken = default, long? maxBytes = null) =>
        ObserveAsync(() => inner.DownloadAsync(id, bucket, key, localPath, progress, cancellationToken, maxBytes), cancellationToken);
    /// <inheritdoc />
    public Task CreateFolderAsync(string id, ObjectStorageBucket bucket, string prefix, string name, CancellationToken token = default) =>
        ObserveAsync(() => inner.CreateFolderAsync(id, bucket, prefix, name, token), token);
    /// <inheritdoc />
    public Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string id, ObjectStorageBucket bucket, IReadOnlyList<ObjectStorageEntry> entries, CancellationToken token = default) =>
        ObserveAsync(() => inner.PrepareDeletionAsync(id, bucket, entries, token), token);
    /// <inheritdoc />
    public Task DeleteAsync(string id, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan, CancellationToken token = default) => ObserveAsync(() => inner.DeleteAsync(id, bucket, plan, token), token);
    /// <inheritdoc />
    public Task<string> CreateTemporaryLinkAsync(string id, ObjectStorageBucket bucket, string key, TimeSpan lifetime, CancellationToken token = default) =>
        ObserveAsync(() => inner.CreateTemporaryLinkAsync(id, bucket, key, lifetime, token), token);

    /// <summary>只以服务层稳定错误码识别连接故障，不检查或显示 SDK 响应正文。</summary>
    private static bool IsConnectionFailure(Exception error) => error is ObjectStorageException { ErrorCode: "AuthenticationFailed" or "ServiceUnavailable" or "RequestTimeout" or "StorageOperationFailed" or "ClockSkew" };
    private async Task<T> ObserveAsync<T>(Func<Task<T>> request, CancellationToken token)
    {
        try { return await request().ConfigureAwait(false); }
        catch (Exception error) when (!token.IsCancellationRequested && IsConnectionFailure(error)) { failed(error); throw; }
    }
    private async Task ObserveAsync(Func<Task> request, CancellationToken token) => await ObserveAsync(async () => { await request().ConfigureAwait(false); return true; }, token).ConfigureAwait(false);
}
