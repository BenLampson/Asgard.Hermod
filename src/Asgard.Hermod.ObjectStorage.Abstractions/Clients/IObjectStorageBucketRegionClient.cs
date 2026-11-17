namespace Asgard.Hermod.ObjectStorage.Abstractions.Clients;

/// <summary>用于从已配置端点发现指定 Bucket 实际地域的可选能力。</summary>
public interface IObjectStorageBucketRegionClient : IObjectStorageClient
{
    /// <summary>只返回指定 Bucket 的地域；凭据不能发现地域时返回空并保留已配置端点。</summary>
    Task<string?> FindBucketRegionAsync(string bucket, CancellationToken cancellationToken);
}
