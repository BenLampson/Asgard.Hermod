using Amazon.S3;
using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>将官方 S3 SDK 的读写能力转换为 Hermod 的对象存储契约。</summary>
internal sealed partial class S3ObjectStorageClient(IAmazonS3 client, S3ConnectionSettings settings, S3ProviderProfile profile)
    : IObjectStorageConnectionProbeClient
{
    /// <inheritdoc />
    public Task<IReadOnlyList<ObjectStorageBucket>> ListBucketsAsync(CancellationToken cancellationToken) => S3Errors.ExecuteAsync<IReadOnlyList<ObjectStorageBucket>>(async () =>
    {
        using var deadline = ReadDeadline(cancellationToken);
        if (settings.Bucket is { } selected)
        {
            await client.ListObjectsV2Async(new() { BucketName = selected, MaxKeys = 1 }, deadline.Token).ConfigureAwait(false);
            return [new(selected, settings.Region)];
        }
        var buckets = new List<ObjectStorageBucket>();
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        do
        {
            var response = await client.ListBucketsAsync(new ListBucketsRequest { ContinuationToken = cursor, MaxBuckets = 1000 }, deadline.Token).ConfigureAwait(false);
            foreach (var bucket in response.Buckets ?? [])
            {
                S3ConnectionSettings.ValidateBucket(bucket.BucketName);
                string? region = bucket.BucketRegion;
                if (profile.DiscoverBucketRegion && string.IsNullOrEmpty(region))
                {
                    var location = await client.GetBucketLocationAsync(new GetBucketLocationRequest() { BucketName = bucket.BucketName }, deadline.Token).ConfigureAwait(false);
                    region = location.Location?.Value;
                    region = region switch { null or "" => "us-east-1", "EU" => "eu-west-1", _ => region };
                }
                region = string.IsNullOrEmpty(region) ? settings.Region : region;
                S3ConnectionSettings.ValidateRegion(region);
                buckets.Add(new(bucket.BucketName, region));
            }
            cursor = response.ContinuationToken;
            if (!string.IsNullOrEmpty(cursor) && !tokens.Add(cursor)) throw S3Errors.InvalidResponse();
        } while (!string.IsNullOrEmpty(cursor));
        return buckets.AsReadOnly();
    }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStoragePage> ListObjectsAsync(string bucket, string prefix, string? continuationToken,
        string? delimiter, int pageSize, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        using var deadline = ReadDeadline(cancellationToken);
        var response = await client.ListObjectsV2Async(new()
        {
            BucketName = bucket,
            Prefix = prefix,
            ContinuationToken = continuationToken,
            Delimiter = delimiter,
            MaxKeys = Math.Clamp(pageSize, 1, 1000),
        }, deadline.Token).ConfigureAwait(false);
        var entries = new Dictionary<string, ObjectStorageEntry>(StringComparer.Ordinal);
        foreach (var common in response.CommonPrefixes ?? []) entries[common] = new(common, true);
        foreach (var item in response.S3Objects ?? [])
        {
            if (item.Key == prefix) continue;
            entries[item.Key] = new(item.Key, item.Key.EndsWith('/'), item.Size ?? 0,
                Timestamp(item.LastModified), StorageClass(item.StorageClass?.Value));
        }
        string? next = response.IsTruncated == true ? response.NextContinuationToken : null;
        if (response.IsTruncated == true && (string.IsNullOrEmpty(next) || next == continuationToken)) throw S3Errors.InvalidResponse();
        return new ObjectStoragePage(entries.Values.OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal).ToArray(), next);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStorageObjectDetails> GetObjectMetadataAsync(string bucket, string key, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        using var deadline = ReadDeadline(cancellationToken);
        var response = await client.GetObjectMetadataAsync(new() { BucketName = bucket, Key = key }, deadline.Token).ConfigureAwait(false);
        // HEAD 不要求额外 ACL 管理权限；对象的有效权限还受桶策略及身份策略共同约束。
        return new ObjectStorageObjectDetails(key, response.ContentLength, Timestamp(response.LastModified),
            StorageClass(response.StorageClass?.Value), response.Headers.ContentType ?? "application/octet-stream", Text.Get("StorageAccessPolicyHint"));
    }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> ObjectExistsAsync(string bucket, string key, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        using var deadline = ReadDeadline(cancellationToken);
        try { await client.GetObjectMetadataAsync(new() { BucketName = bucket, Key = key }, deadline.Token).ConfigureAwait(false); return true; }
        catch (Exception error) when (S3Errors.Missing(error)) { return false; }
    }, cancellationToken);

    /// <inheritdoc />
    public Task<ObjectStorageReadResult> OpenReadAsync(string bucket, string key, int? maxBytes, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        if (maxBytes is not null) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes.Value);
        var response = await client.GetObjectAsync(new()
        {
            BucketName = bucket,
            Key = key,
            ByteRange = maxBytes is { } count ? new ByteRange(0, count - 1) : null,
        }, cancellationToken).ConfigureAwait(false);
        return new ObjectStorageReadResult(new S3ResponseStream(response), response.ContentLength);
    }, cancellationToken);

    /// <inheritdoc />
    public Task DeleteAsync(string bucket, IReadOnlyList<string> keys, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        foreach (var batch in keys.Chunk(1000))
        {
            var result = await client.DeleteObjectsAsync(new()
            {
                BucketName = bucket,
                Objects = batch.Select(key => new KeyVersion { Key = key }).ToList(),
                Quiet = false,
            }, cancellationToken).ConfigureAwait(false);
            if (result.DeleteErrors is { Count: > 0 })
                throw ObjectStorageException.FromResource("StorageDeletePartialFailure", "PartialDeleteFailure");
        }
        return true;
    }, cancellationToken);

    /// <inheritdoc />
    public string CreateTemporaryLink(string bucket, string key, TimeSpan lifetime)
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        if (lifetime <= TimeSpan.Zero || lifetime > TimeSpan.FromDays(7))
            throw ObjectStorageException.FromResource("StorageLinkLifetimeInvalid", "InvalidLinkLifetime");
        try
        {
            return client.GetPreSignedURL(new()
            {
                BucketName = bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(lifetime),
                Protocol = settings.Endpoint.Scheme == "http" ? Protocol.HTTP : Protocol.HTTPS,
            });
        }
        catch (Exception error) { throw S3Errors.Translate(error); }
    }

    /// <summary>元数据请求使用短期限，大文件流的期限由调用方和传输层管理。</summary>
    private static CancellationTokenSource ReadDeadline(CancellationToken token)
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        return deadline;
    }

    private static DateTimeOffset? Timestamp(DateTime? value) => value is null ? null : new(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
    private static string StorageClass(string? value) => value switch
    {
        null or "" or "STANDARD" => "Standard",
        "STANDARD_IA" => "IA",
        "GLACIER" or "ARCHIVE" => "Archive",
        "DEEP_ARCHIVE" => "DeepColdArchive",
        _ => value,
    };

    /// <inheritdoc />
    public void Dispose() => client.Dispose();
}
