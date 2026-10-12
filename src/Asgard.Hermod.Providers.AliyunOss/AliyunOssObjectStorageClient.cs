using Oss = AlibabaCloud.OSS.V2;
using OssModels = AlibabaCloud.OSS.V2.Models;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>将官方 SDK 的远程对象能力转换为通用资源模型与安全错误。</summary>
public sealed partial class AliyunOssObjectStorageClient(Oss.Client client) : IObjectStorageConnectionProbeClient
{
    /// <summary>元数据和列表请求采用短期限；流上传由工厂提供较长传输期限。</summary>
    private static readonly Oss.OperationOptions ReadOptions = new() { ReadWriteTimeout = TimeSpan.FromSeconds(20) };

    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync<IReadOnlyList<ObjectStorageBucket>>(async () =>
        {
            var buckets = new Dictionary<string, ObjectStorageBucket>(StringComparer.Ordinal);
            var seenMarkers = new HashSet<string>(StringComparer.Ordinal);
            string? marker = null;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OssModels.ListBucketsResult result = await client.ListBucketsAsync(new() { MaxKeys = 100, Marker = marker },
                    ReadOptions, cancellationToken).ConfigureAwait(false);
                foreach (OssModels.BucketProperties bucket in result.Buckets ?? [])
                {
                    if (string.IsNullOrWhiteSpace(bucket.Name)) throw InvalidResponse();
                    AliyunOssEndpoint.ValidateBucket(bucket.Name);
                    string region = AliyunOssEndpoint.NormalizeRegion(bucket.Region ?? bucket.Location);
                    buckets[bucket.Name] = new(bucket.Name, region, bucket.StorageClass ?? "Standard");
                }
                if (result.IsTruncated != true) break;
                marker = result.NextMarker;
                if (string.IsNullOrEmpty(marker) || !seenMarkers.Add(marker)) throw InvalidResponse();
            }
            return buckets.Values.OrderBy(bucket => bucket.Name, StringComparer.Ordinal).ToArray();
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string bucket, string prefix, string? continuationToken,
        string? delimiter, int pageSize, CancellationToken cancellationToken) => AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            OssModels.ListObjectsV2Result result = await client.ListObjectsV2Async(new()
            {
                Bucket = bucket,
                Prefix = prefix,
                Delimiter = delimiter,
                ContinuationToken = continuationToken,
                MaxKeys = pageSize,
            }, ReadOptions, cancellationToken).ConfigureAwait(false);
            var entries = new Dictionary<string, ObjectStorageEntry>(StringComparer.Ordinal);
            foreach (OssModels.CommonPrefix directory in result.CommonPrefixes ?? [])
                if (!string.IsNullOrEmpty(directory.Prefix)) entries[directory.Prefix] = new(directory.Prefix, true);
            foreach (OssModels.ObjectSummary item in result.Contents ?? [])
            {
                if (string.IsNullOrEmpty(item.Key)) throw InvalidResponse();
                entries[item.Key] = new(item.Key, item.Key.EndsWith('/'), item.Size ?? 0,
                    ToTimestamp(item.LastModified), item.StorageClass ?? "Standard");
            }
            string? token = result.IsTruncated == true ? result.NextContinuationToken : null;
            if (result.IsTruncated == true && string.IsNullOrEmpty(token)) throw InvalidResponse();
            return new ObjectStoragePage(entries.Values.OrderByDescending(entry => entry.IsDirectory)
                .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray(), token);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectMetadataAsync(string bucket, string key, CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            OssModels.HeadObjectResult metadata = await client.HeadObjectAsync(new() { Bucket = bucket, Key = key },
                ReadOptions, cancellationToken).ConfigureAwait(false);
            string accessControl;
            try
            {
                OssModels.GetObjectAclResult acl = await client.GetObjectAclAsync(new() { Bucket = bucket, Key = key },
                    ReadOptions, cancellationToken).ConfigureAwait(false);
                accessControl = acl.Acl ?? "未知";
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && AliyunOssErrors.IsAccessDenied(error))
            {
                // 对象读取权限不必包含 ACL 管理权限，保留已经确认的 HEAD 元信息。
                accessControl = "读取权限未知";
            }
            return new ObjectStorageObjectDetails(key, metadata.ContentLength ?? 0, ParseTimestamp(metadata.LastModified),
                metadata.StorageClass ?? "Standard", metadata.ContentType ?? "application/octet-stream", accessControl);
        }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            try
            {
                await client.HeadObjectAsync(new() { Bucket = bucket, Key = key }, ReadOptions, cancellationToken).ConfigureAwait(false);
                return true;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && AliyunOssErrors.IsMissingObject(error))
            {
                return false;
            }
        }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStorageReadResult> OpenReadAsync(string bucket, string key, int? maxBytes, CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            OssModels.GetObjectResult result = await client.GetObjectAsync(new()
            {
                Bucket = bucket,
                Key = key,
                Range = maxBytes is > 0 ? $"bytes=0-{maxBytes - 1}" : null,
            }, ReadOptions, cancellationToken).ConfigureAwait(false);
            if (result.Body is null) throw InvalidResponse();
            return new ObjectStorageReadResult(result.Body, result.ContentLength ?? -1);
        }, cancellationToken);

    /// <inheritdoc />
    public Task UploadAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken) => AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            ObjectStorageProgress.Report(progress, 0, size);
            if (size > AliyunOssMultipartUploader.MinimumPartSize)
                await new AliyunOssMultipartUploader(client).UploadAsync(bucket, key, body, size, overwrite, progress,
                    cancellationToken).ConfigureAwait(false);
            else
                await client.PutObjectAsync(new()
                {
                    Bucket = bucket,
                    Key = key,
                    Body = body,
                    ContentLength = size,
                    ForbidOverwrite = !overwrite,
                    ProgressFn = (_, transferred, _) => ObjectStorageProgress.Report(progress, transferred, size),
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
            ObjectStorageProgress.Report(progress, size, size);
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string bucket, IReadOnlyList<string> keys, CancellationToken cancellationToken) =>
        AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            if (keys.Count == 0) return true;
            OssModels.DeleteMultipleObjectsResult result = await client.DeleteMultipleObjectsAsync(new()
            {
                Bucket = bucket,
                Quiet = false,
                Objects = keys.Select(key => new OssModels.DeleteObject { Key = key }).ToArray(),
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            HashSet<string> deleted = (result.DeletedObjects ?? []).Select(item => item.Key ?? "").ToHashSet(StringComparer.Ordinal);
            if (keys.Any(key => !deleted.Contains(key)))
                throw new ObjectStorageException("部分对象删除失败，请刷新列表后重新确认剩余对象。", "PartialDelete");
            return true;
        }, cancellationToken);

    /// <inheritdoc />
    public string CreateTemporaryLink(string bucket, string key, TimeSpan lifetime)
    {
        AliyunOssEndpoint.ValidateBucket(bucket);
        try
        {
            OssModels.PresignResult result = client.Presign(new OssModels.GetObjectRequest { Bucket = bucket, Key = key },
                DateTime.UtcNow.Add(lifetime));
            return result.Url ?? throw InvalidResponse();
        }
        catch (ObjectStorageException) { throw; }
        catch (Exception error) { throw AliyunOssErrors.Translate(error); }
    }

    /// <summary>SDK 返回的 UTC 日期转为统一元数据模型。</summary>
    private static DateTimeOffset? ToTimestamp(DateTime? value) => value is null ? null :
        new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    /// <summary>HTTP 日期缺失时保持未知，不猜测对象更新时间。</summary>
    private static DateTimeOffset? ParseTimestamp(string? value) =>
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset result) ? result : null;
    /// <summary>无效响应不泄露正文或请求详情。</summary>
    private static ObjectStorageException InvalidResponse() => new("OSS 返回了无法验证的对象响应，请稍后重试。", "InvalidResponse");
    /// <inheritdoc />
    public void Dispose() => client.Dispose();
}
