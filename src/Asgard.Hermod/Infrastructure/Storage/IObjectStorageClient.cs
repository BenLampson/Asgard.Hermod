namespace Asgard.Hermod.Infrastructure.Storage;

/// <summary>第三方资源适配层，只提供远程能力；文件路径和删除确认由应用服务管理。</summary>
public interface IObjectStorageClient : IDisposable
{
    /// <summary>读取全部容器，连接器负责遍历服务商分页。</summary>
    Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(CancellationToken cancellationToken);
    /// <summary>读取一页对象；分隔符为空时枚举整个目录子树。</summary>
    Task<ObjectStoragePage> ListObjectsAsync(string bucket, string prefix, string? continuationToken,
        string? delimiter, int pageSize, CancellationToken cancellationToken);
    /// <summary>读取元数据和 ACL，不下载对象正文。</summary>
    Task<ObjectStorageObjectDetails> GetObjectMetadataAsync(string bucket, string key, CancellationToken cancellationToken);
    /// <summary>只将明确不存在的对象视为 false，权限和网络失败仍为失败。</summary>
    Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken);
    /// <summary>打开完整或限量只读流，调用方负责释放正文。</summary>
    Task<ObjectStorageReadResult> OpenReadAsync(string bucket, string key, int? maxBytes, CancellationToken cancellationToken);
    /// <summary>上传只读流；覆盖未授权时必须在远程服务端拒绝覆盖。</summary>
    Task UploadAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken);
    /// <summary>删除一个明确批次，不扩展目录；部分成功必须反馈失败。</summary>
    Task DeleteAsync(string bucket, IReadOnlyList<string> keys, CancellationToken cancellationToken);
    /// <summary>只生成 GET 签名，不能通过该入口创建写入链接。</summary>
    string CreateTemporaryLink(string bucket, string key, TimeSpan lifetime);
}
