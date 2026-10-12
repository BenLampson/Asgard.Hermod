namespace Asgard.Hermod.ObjectStorage.Abstractions.Clients;

/// <summary>连接探测采用版本回执和精确版本清理，避免普通键删除在版本化容器中留下历史数据。</summary>
public interface IObjectStorageConnectionProbeClient : IObjectStorageClient
{
    /// <summary>使用独立随机键写入小型探测对象，拒绝既有对象并保留明确拒绝的安全错误码。</summary>
    Task<ObjectStorageProbeWriteResult> UploadProbeAsync(string bucket, string key, Stream body, long size,
        CancellationToken token);

    /// <summary>回读指定版本；版本未知时读当前对象，并返回实际版本帮助确认未知上传的归属。</summary>
    Task<ObjectStorageProbeReadResult> OpenProbeReadAsync(string bucket, string key, string? versionId,
        int maxBytes, CancellationToken token);

    /// <summary>只清理已确认属于本次测试的精确版本；字面字符串 null 与缺失版本参数含义不同。</summary>
    Task DeleteProbeAsync(string bucket, string key, string? versionId, CancellationToken token);
}
