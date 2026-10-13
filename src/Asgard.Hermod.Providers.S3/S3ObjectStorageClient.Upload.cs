using Amazon.S3;
using Amazon.S3.Model;

namespace Asgard.Hermod.Providers.S3;

/// <summary>上传提交条件、分片进度和失败清理均位于协议适配层。</summary>
internal sealed partial class S3ObjectStorageClient
{
    /// <summary>顺序发送八 MiB 起步的分片，内存占用不随文件总大小增长。</summary>
    private const long MinimumPartSize = 8 * 1024 * 1024;

    /// <inheritdoc />
    public Task UploadAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken) => S3Errors.ExecuteAsync(async () =>
    {
        ValidateUpload(bucket, body, size);
        if (!overwrite && profile.ForbidOverwriteHeader is not null)
        {
            // COS/TOS 的原生禁止覆盖头不能保护版本化桶，不能以客户端先查询代替原子条件。
            using var deadline = ReadDeadline(cancellationToken);
            var versioning = await client.GetBucketVersioningAsync(new GetBucketVersioningRequest() { BucketName = bucket }, deadline.Token).ConfigureAwait(false);
            if (versioning.VersioningConfig?.Status is { } status && status != VersionStatus.Off)
                throw ObjectStorageException.FromResource("StorageConditionalVersionedUnsupported", "ConditionalWriteUnsupported");
        }
        if (size <= MinimumPartSize)
            await PutAsync(bucket, key, body, size, overwrite, progress, cancellationToken).ConfigureAwait(false);
        else
            await MultipartAsync(bucket, key, body, size, overwrite, progress, cancellationToken).ConfigureAwait(false);
        ObjectStorageProgress.Report(progress, size, size);
        return true;
    }, cancellationToken);

    /// <summary>小文件采用 SDK PUT；调用方拥有源流，SDK 不得关闭它。</summary>
    private async Task<PutObjectResponse> PutAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken token)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = body,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            UseChunkEncoding = false,
            IfNoneMatch = overwrite ? null : "*",
        };
        request.Headers.ContentLength = size;
        request.StreamTransferProgress += (_, args) => ObjectStorageProgress.Report(progress, args.TransferredBytes, size);
        return await client.PutObjectAsync(request, token).ConfigureAwait(false);
    }

    /// <summary>失败和取消都在独立期限内中止上传，最终提交仍携带服务端禁止覆盖条件。</summary>
    private async Task MultipartAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken token)
    {
        long partSize = Math.Max(MinimumPartSize, (size + 9999) / 10000);
        if (partSize > 5L * 1024 * 1024 * 1024)
            throw ObjectStorageException.FromResource("StorageFileTooLarge", "FileTooLarge");
        var initiated = await client.InitiateMultipartUploadAsync(new() { BucketName = bucket, Key = key }, token).ConfigureAwait(false);
        if (string.IsNullOrEmpty(initiated.UploadId)) throw S3Errors.InvalidResponse();
        bool completed = false;
        try
        {
            var parts = new List<PartETag>();
            long initialPosition = body.Position;
            for (long offset = 0; offset < size; offset += partSize)
            {
                long start = offset;
                long length = Math.Min(partSize, size - offset);
                using var partBody = new S3SliceStream(body, initialPosition + offset, length);
                var request = new UploadPartRequest
                {
                    BucketName = bucket,
                    Key = key,
                    UploadId = initiated.UploadId,
                    PartNumber = parts.Count + 1,
                    InputStream = partBody,
                    PartSize = length,
                    UseChunkEncoding = false,
                };
                request.StreamTransferProgress += (_, args) => ObjectStorageProgress.Report(progress, start + args.TransferredBytes, size);
                var response = await client.UploadPartAsync(request, token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(response.ETag)) throw S3Errors.InvalidResponse();
                parts.Add(new(parts.Count + 1, response.ETag));
            }
            token.ThrowIfCancellationRequested();
            await client.CompleteMultipartUploadAsync(new()
            {
                BucketName = bucket,
                Key = key,
                UploadId = initiated.UploadId,
                PartETags = parts,
                IfNoneMatch = overwrite ? null : "*",
            }, token).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await client.AbortMultipartUploadAsync(new()
                    {
                        BucketName = bucket,
                        Key = key,
                        UploadId = initiated.UploadId,
                    }, cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
            }
        }
    }

    /// <summary>固定上传长度并要求可定位源流，避免 SDK 重读或分片时误读数据。</summary>
    private static void ValidateUpload(string bucket, Stream body, long size)
    {
        S3ConnectionSettings.ValidateBucket(bucket);
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        if (!body.CanRead || !body.CanSeek || body.Length - body.Position != size)
            throw ObjectStorageException.FromResource("TheUploadSourceStreamMustBeReadableAndSeekable", "InvalidUploadStream");
    }
}
