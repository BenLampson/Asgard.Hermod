using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.Providers.AliyunOss;

public sealed partial class AliyunOssObjectStorageClient
{
    private static readonly Oss.OperationOptions RegionLookupOptions = new() { ReadWriteTimeout = TimeSpan.FromSeconds(5) };

    /// <inheritdoc />
    public Task<string?> FindBucketRegionAsync(string bucket, CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync<string?>(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            try
            {
                var result = await client.GetBucketLocationAsync(new() { Bucket = bucket },
                    RegionLookupOptions, cancellationToken).ConfigureAwait(false);
                return AliyunOssEndpoint.NormalizeRegion(result.LocationConstraint);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && AliyunOssErrors.IsAccessDenied(error))
            {
                // 请求错地域时，OSS 使用专用错误码在 HostId 中返回该 Bucket 的正确域名。
                if (AliyunOssErrors.BucketRegionHint(error, bucket) is { } hinted) return hinted;
            }

            try
            {
                // 不能查询单个 Bucket 位置时，只查其名称前缀。使用结果中的精确名称匹配。
                string? marker = null;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (int page = 0; page < 8; page++)
                {
                    var result = await client.ListBucketsAsync(new() { Prefix = bucket, MaxKeys = 100, Marker = marker },
                        RegionLookupOptions, cancellationToken).ConfigureAwait(false);
                    var match = result.Buckets?.FirstOrDefault(item => item.Name == bucket);
                    if (match is not null) return AliyunOssEndpoint.NormalizeRegion(match.Region ?? match.Location);
                    if (result.IsTruncated != true) return null;
                    marker = result.NextMarker;
                    if (string.IsNullOrEmpty(marker) || !seen.Add(marker)) throw InvalidResponse();
                }
                return null;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && AliyunOssErrors.IsAccessDenied(error))
            {
                // 只授权数据操作的凭据仍可访问已正确配置地域的 Bucket。
                return null;
            }
        }, cancellationToken);
}
