namespace Asgard.Hermod.UI.Tests;

/// <summary>可控制迟到返回、业务错误和传输的对象服务替身，不访问网络或磁盘。</summary>
internal sealed class ObjectStorageTestService : IObjectStorageService
{
    /// <summary>默认可见桶，测试可以替换。</summary>
    internal IReadOnlyList<ObjectStorageBucket> Buckets { get; set; } = [new("assets-dev", "cn-hangzhou")];
    /// <summary>桶读取可阻塞或失败，支持取消生命周期测试。</summary>
    internal Func<string, CancellationToken, Task<IReadOnlyList<ObjectStorageBucket>>>? ListBucketsHandler { get; set; }
    /// <summary>按查询精确控制每个目录、搜索或下一页，允许故意忽略取消模拟迟到响应。</summary>
    internal Func<string, ObjectStorageBucket, string, string, string?, CancellationToken, Task<ObjectStoragePage>>? ListObjectsHandler { get; set; }
    /// <summary>对象详情可模拟选择改变时的迟到返回。</summary>
    internal Func<string, ObjectStorageBucket, string, CancellationToken, Task<ObjectStorageObjectDetails>>? DetailsHandler { get; set; }
    /// <summary>同名判断失败不能被当成不存在。</summary>
    internal Func<string, CancellationToken, Task<bool>>? ExistsHandler { get; set; }
    /// <summary>传输回调由测试报告进度和失败。</summary>
    internal Func<string, bool, IProgress<ObjectStorageTransferProgress>?, CancellationToken, Task>? UploadHandler { get; set; }
    /// <summary>下载回调可模拟取消。</summary>
    internal Func<string, string, IProgress<ObjectStorageTransferProgress>?, CancellationToken, Task>? DownloadHandler { get; set; }
    /// <summary>准备删除返回完整展开后的目录对象键。</summary>
    internal Func<IReadOnlyList<ObjectStorageEntry>, CancellationToken, Task<ObjectStorageDeletePlan>>? PrepareDeleteHandler { get; set; }
    /// <summary>删除失败和部分失败由测试指定。</summary>
    internal Func<ObjectStorageDeletePlan, CancellationToken, Task>? DeleteHandler { get; set; }
    /// <summary>创建目录可以失败或记录业务行为。</summary>
    internal Func<string, string, CancellationToken, Task>? CreateFolderHandler { get; set; }
    /// <summary>生成链接必须只在用户触发回调后调用。</summary>
    internal Func<string, TimeSpan, CancellationToken, Task<string>>? LinkHandler { get; set; }
    /// <summary>记录精确的列表查询。</summary>
    internal List<(string Bucket, string Prefix, string Search, string? Continuation)> ListCalls { get; } = [];
    /// <summary>记录覆盖参数及对象键。</summary>
    internal List<(string Key, bool Overwrite)> UploadCalls { get; } = [];
    /// <summary>仅记录真正发出的删除计划。</summary>
    internal List<ObjectStorageDeletePlan> DeletedPlans { get; } = [];
    /// <summary>记录详情查询的真实键，避免排序后测试误用行索引。</summary>
    internal List<string> DetailsCalls { get; } = [];
    /// <summary>容器读取次数。</summary>
    internal int BucketReads { get; private set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        BucketReads++;
        return ListBucketsHandler?.Invoke(instanceId, cancellationToken) ?? Task.FromResult(Buckets);
    }
    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string instanceId, ObjectStorageBucket bucket, string prefix,
        string search = "", string? continuationToken = null, CancellationToken cancellationToken = default)
    {
        ListCalls.Add((bucket.Name, prefix, search, continuationToken));
        return ListObjectsHandler?.Invoke(instanceId, bucket, prefix, search, continuationToken, cancellationToken)
            ?? Task.FromResult(new ObjectStoragePage([]));
    }
    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectDetailsAsync(string instanceId, ObjectStorageBucket bucket, string key, CancellationToken cancellationToken = default)
    {
        DetailsCalls.Add(key);
        return DetailsHandler?.Invoke(instanceId, bucket, key, cancellationToken)
            ?? Task.FromResult(new ObjectStorageObjectDetails(key, 128, null, "Standard", "application/json", "私有", "{}"));
    }
    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string instanceId, ObjectStorageBucket bucket, string key, CancellationToken cancellationToken = default) =>
        ExistsHandler?.Invoke(key, cancellationToken) ?? Task.FromResult(false);
    /// <inheritdoc />
    public Task UploadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        bool overwrite, IProgress<ObjectStorageTransferProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        UploadCalls.Add((key, overwrite));
        return UploadHandler?.Invoke(key, overwrite, progress, cancellationToken) ?? Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task DownloadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        IProgress<ObjectStorageTransferProgress>? progress = null, CancellationToken cancellationToken = default) =>
        DownloadHandler?.Invoke(key, localPath, progress, cancellationToken) ?? Task.CompletedTask;
    /// <inheritdoc />
    public Task CreateFolderAsync(string instanceId, ObjectStorageBucket bucket, string prefix, string name, CancellationToken cancellationToken = default) =>
        CreateFolderHandler?.Invoke(prefix, name, cancellationToken) ?? Task.CompletedTask;
    /// <inheritdoc />
    public Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string instanceId, ObjectStorageBucket bucket,
        IReadOnlyList<ObjectStorageEntry> entries, CancellationToken cancellationToken = default) =>
        PrepareDeleteHandler?.Invoke(entries, cancellationToken) ?? Task.FromResult(new ObjectStorageDeletePlan(entries.Select(entry => entry.Key).ToArray()));
    /// <inheritdoc />
    public Task DeleteAsync(string instanceId, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan, CancellationToken cancellationToken = default)
    {
        DeletedPlans.Add(plan);
        return DeleteHandler?.Invoke(plan, cancellationToken) ?? Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task<string> CreateTemporaryLinkAsync(string instanceId, ObjectStorageBucket bucket, string key,
        TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        LinkHandler?.Invoke(key, lifetime, cancellationToken) ?? Task.FromResult("https://example.invalid/temporary");
}
