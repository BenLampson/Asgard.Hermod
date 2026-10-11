namespace Asgard.Hermod.UI.Services;

/// <summary>以已保存的实例标识操作对象存储，界面不持有服务商 SDK 或访问密钥。</summary>
public interface IObjectStorageService
{
    /// <summary>读取账户下可见的全部容器和各自区域。</summary>
    Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>只读取当前目录的一批对象，搜索基于目录前缀，不统计整个容器。</summary>
    Task<ObjectStoragePage> ListObjectsAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, string search = "", string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>读取元数据，并仅为适合的文本提供最多 256KB 的预览。</summary>
    Task<ObjectStorageObjectDetails> GetObjectDetailsAsync(string instanceId,
        ObjectStorageBucket bucket, string key, CancellationToken cancellationToken = default);

    /// <summary>检查同名对象是否存在，权限失败须保留为失败而非当作不存在。</summary>
    Task<bool> ObjectExistsAsync(string instanceId, ObjectStorageBucket bucket, string key,
        CancellationToken cancellationToken = default);

    /// <summary>从本地文件上传并报告进度；未获覆盖授权时由远端拒绝覆盖。</summary>
    Task UploadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        bool overwrite, IProgress<ObjectStorageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>下载到用户明确选择的位置，取消或失败时清理未完成的文件。</summary>
    Task DownloadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        IProgress<ObjectStorageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>创建以斜杠结尾的目录占位对象，不覆盖当前目录下的重名对象。</summary>
    Task CreateFolderAsync(string instanceId, ObjectStorageBucket bucket, string prefix, string name,
        CancellationToken cancellationToken = default);

    /// <summary>枚举所选目录的所有对象，返回供用户确认的明确删除范围。</summary>
    Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string instanceId, ObjectStorageBucket bucket,
        IReadOnlyList<ObjectStorageEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>仅删除已经确认的对象键；部分失败需显式告知，刷新后可重新确认。</summary>
    Task DeleteAsync(string instanceId, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan,
        CancellationToken cancellationToken = default);

    /// <summary>生成有限有效期的只读临时链接，不自动发布或发送给其他人。</summary>
    Task<string> CreateTemporaryLinkAsync(string instanceId, ObjectStorageBucket bucket, string key,
        TimeSpan lifetime, CancellationToken cancellationToken = default);
}
