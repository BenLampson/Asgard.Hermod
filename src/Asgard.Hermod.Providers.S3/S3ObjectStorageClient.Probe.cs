using Amazon.S3;
using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>随机探针保留写入版本并精确清理，未知结果由通用探测器通过随机正文确认归属。</summary>
internal sealed partial class S3ObjectStorageClient
{
    /// <inheritdoc />
    public async Task<ObjectStorageProbeWriteResult> UploadProbeAsync(string bucket, string key, Stream body, long size, CancellationToken token)
    {
        ValidateUpload(bucket, body, size);
        // 即使服务商在版本化桶内忽略原生禁止覆盖头，也先验证随机键不存在。
        try
        {
            if (await ObjectExistsAsync(bucket, key, token).ConfigureAwait(false))
                throw ObjectStorageException.FromResource("TheRandomTestObjectAlreadyExistsNoWriteOrDeletionWasAttempted", "FileAlreadyExists");
            token.ThrowIfCancellationRequested();
        }
        catch (ObjectStorageException error) when (error.ErrorCode == "FileAlreadyExists") { throw; }
        catch (Exception)
        {
            throw ObjectStorageException.FromResource("ThePreWriteCheckFailedNoTestObjectCreationWasAttempted", "ProbePreflightFailed");
        }
        try
        {
            var result = await PutAsync(bucket, key, body, size, false, null, token).ConfigureAwait(false);
            return new(result.VersionId);
        }
        catch (AmazonS3Exception error) when ((int)error.StatusCode is >= 400 and < 500)
        {
            // 明确拒绝优先于同时取消，禁止对未写入的既有对象执行清理。
            var safe = S3Errors.Translate(error);
            throw safe.ErrorCode is "AccessDenied" or "AuthenticationFailed" or "NoSuchBucket" or "FileAlreadyExists" or "PreconditionFailed"
                ? safe : ObjectStorageException.FromResource("StorageProbeWriteRejected", "ProbeWriteRejected");
        }
        catch (Exception error)
        {
            token.ThrowIfCancellationRequested();
            throw S3Errors.Translate(error);
        }
    }

    /// <inheritdoc />
    public Task<ObjectStorageProbeReadResult> OpenProbeReadAsync(string bucket, string key, string? versionId,
        int maxBytes, CancellationToken token) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        var response = await client.GetObjectAsync(new()
        {
            BucketName = bucket,
            Key = key,
            VersionId = versionId,
            ByteRange = new ByteRange(0, maxBytes - 1),
        }, token).ConfigureAwait(false);
        return new ObjectStorageProbeReadResult(new(new S3ResponseStream(response), response.ContentLength), response.VersionId);
    }, token);

    /// <inheritdoc />
    public Task DeleteProbeAsync(string bucket, string key, string? versionId, CancellationToken token) => S3Errors.ExecuteAsync(async () =>
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        var response = await client.DeleteObjectAsync(new() { BucketName = bucket, Key = key, VersionId = versionId }, token).ConfigureAwait(false);
        // 字面字符串 null 是有效版本号；未指定版本时创建删除标记不代表永久清理。
        if (response.DeleteMarker == "true" || versionId is not null && response.VersionId is not null && response.VersionId != versionId)
            throw ObjectStorageException.FromResource("StorageProbeCleanupIncomplete", "ProbeCleanupIncomplete");
        return true;
    }, token);
}
