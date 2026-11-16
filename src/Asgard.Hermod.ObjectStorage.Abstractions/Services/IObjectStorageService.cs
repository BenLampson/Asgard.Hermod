namespace Asgard.Hermod.ObjectStorage.Abstractions.Services;

/// <summary>以已保存的实例标识操作对象存储，界面不持有服务商 SDK 或访问密钥。</summary>
public interface IObjectStorageService
{
    /// <summary>读取账户下可见的全部容器和各自区域。</summary>
    Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(string instanceId,
        CancellationToken cancellationToken = default);

    /// <summary>读取当前目录的一批对象，按文件和文件夹名称包含匹配，支持星号与问号的完整名称通配匹配，忽略大小写；搜索跳过无匹配页，不递归子目录。</summary>
    Task<ObjectStoragePage> ListObjectsAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, string search = "", string? continuationToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>临时扫描整个目录，仅汇总直接子项；超过阈值先确认，拒绝时返回空结果，不发布部分排序。</summary>
    Task<ObjectStorageDirectoryScanResult?> ScanDirectoryAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, Func<ObjectStorageDirectoryScanProgress, CancellationToken, Task<bool>> confirmContinuation,
        IProgress<ObjectStorageDirectoryScanProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>递归只读列举当前前缀，统计文件大小、文件数和去重后的子目录数，进度不代表最终结果。</summary>
    Task<ObjectStorageFolderStatistics> GetFolderStatisticsAsync(string instanceId, ObjectStorageBucket bucket,
        string prefix, IProgress<ObjectStorageFolderStatistics>? progress = null, CancellationToken cancellationToken = default);

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

    /// <summary>下载到明确位置，取消或失败时清理未完成文件；预览可限制实际接收字节数。</summary>
    Task DownloadAsync(string instanceId, ObjectStorageBucket bucket, string key, string localPath,
        IProgress<ObjectStorageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default, long? maxBytes = null);

    /// <summary>创建以斜杠结尾的目录占位对象，不覆盖当前目录下的重名对象。</summary>
    Task CreateFolderAsync(string instanceId, ObjectStorageBucket bucket, string prefix, string name,
        CancellationToken cancellationToken = default);

    /// <summary>展开已确认的选择，按页报告已收集的去重键数，返回完整的删除清单。</summary>
    Task<ObjectStorageDeletePlan> PrepareDeletionAsync(string instanceId, ObjectStorageBucket bucket,
        IReadOnlyList<ObjectStorageEntry> entries, IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>仅删除清单中的键，报告服务已确认的累计键数；部分失败需显式告知。</summary>
    Task DeleteAsync(string instanceId, ObjectStorageBucket bucket, ObjectStorageDeletePlan plan,
        IProgress<int>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>生成有限有效期的只读临时链接，不自动发布或发送给其他人。</summary>
    Task<string> CreateTemporaryLinkAsync(string instanceId, ObjectStorageBucket bucket, string key,
        TimeSpan lifetime, CancellationToken cancellationToken = default);
}
