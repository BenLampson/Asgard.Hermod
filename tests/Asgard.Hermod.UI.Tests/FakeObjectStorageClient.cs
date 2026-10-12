
namespace Asgard.Hermod.UI.Tests;

/// <summary>在远程资源边界注入分页、流和失败行为，不复制 SDK 的实际实现。</summary>
public sealed class FakeObjectStorageClient : IObjectStorageClient
{
    /// <summary>可配置列表行为，目录枚举测试能检查分隔符和游标。</summary>
    public Func<string, string, string?, string?, int, CancellationToken, Task<ObjectStoragePage>>? ListObjects { get; set; }
    /// <summary>可配置元数据和 MIME，用于预览策略验证。</summary>
    public Func<string, string, CancellationToken, Task<ObjectStorageObjectDetails>>? Metadata { get; set; }
    /// <summary>可配置不存在、权限和冲突行为。</summary>
    public Func<string, string, CancellationToken, Task<bool>>? Exists { get; set; }
    /// <summary>可配置正文流，验证原子下载和严格预览上限。</summary>
    public Func<string, string, int?, CancellationToken, Task<ObjectStorageReadResult>>? Read { get; set; }
    /// <summary>上传行为接收可读源流与覆盖授权。</summary>
    public Func<string, string, Stream, long, bool, IProgress<ObjectStorageTransferProgress>?, CancellationToken, Task>? Upload { get; set; }
    /// <summary>删除行为只收到确认对象键。</summary>
    public Func<string, IReadOnlyList<string>, CancellationToken, Task>? Delete { get; set; }
    /// <summary>临时链接只收到固定生命周期。</summary>
    public Func<string, string, TimeSpan, string>? Link { get; set; }
    /// <summary>验证每次业务调用结束后释放客户端。</summary>
    public int DisposeCount { get; private set; }
    /// <summary>模拟提交成功后发生的清理异常。</summary>
    public bool ThrowOnDispose { get; set; }

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<ObjectStorageBucket>>([new("test-bucket", "cn-hangzhou")]);
    }
    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string bucket, string prefix, string? continuationToken,
        string? delimiter, int pageSize, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ListObjects?.Invoke(bucket, prefix, continuationToken, delimiter, pageSize, cancellationToken)
            ?? Task.FromResult(new ObjectStoragePage([]));
    }
    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectMetadataAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Metadata?.Invoke(bucket, key, cancellationToken)
            ?? Task.FromResult(new ObjectStorageObjectDetails(key, 0, null, "Standard", "text/plain", "private"));
    }
    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Exists?.Invoke(bucket, key, cancellationToken) ?? Task.FromResult(false);
    }
    /// <inheritdoc />
    public Task<ObjectStorageReadResult> OpenReadAsync(string bucket, string key, int? maxBytes, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Read?.Invoke(bucket, key, maxBytes, cancellationToken)
            ?? Task.FromResult(new ObjectStorageReadResult(new MemoryStream(), 0));
    }
    /// <inheritdoc />
    public Task UploadAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Upload?.Invoke(bucket, key, body, size, overwrite, progress, cancellationToken) ?? Task.CompletedTask;
    }
    /// <inheritdoc />
    public Task DeleteAsync(string bucket, IReadOnlyList<string> keys, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Delete?.Invoke(bucket, keys, cancellationToken) ?? Task.CompletedTask;
    }
    /// <inheritdoc />
    public string CreateTemporaryLink(string bucket, string key, TimeSpan lifetime) =>
        Link?.Invoke(bucket, key, lifetime) ?? "https://storage.example.test/temporary";
    /// <inheritdoc />
    public void Dispose()
    {
        DisposeCount++;
        if (ThrowOnDispose) throw new IOException("private-resource-token raw client cleanup");
    }
}
