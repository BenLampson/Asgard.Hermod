using Oss = AlibabaCloud.OSS.V2;
using OssModels = AlibabaCloud.OSS.V2.Models;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>按有限分片数上传大文件，完成请求再次携带服务端禁止覆盖条件。</summary>
internal sealed class AliyunOssMultipartUploader(Oss.Client client)
{
    /// <summary>小文件直接 PUT，大文件以八 MiB 起步，超大文件按一万分片上限调整。</summary>
    public const long MinimumPartSize = 8 * 1024 * 1024;

    /// <summary>上传失败或取消时用独立的短期限中止分片，释放未完成的远端资源。</summary>
    public async Task UploadAsync(string bucket, string key, Stream body, long size, bool overwrite,
        IProgress<ObjectStorageTransferProgress>? progress, CancellationToken cancellationToken)
    {
        long partSize = Math.Max(MinimumPartSize, (size + 9999) / 10000);
        if (partSize > 5L * 1024 * 1024 * 1024)
            throw ObjectStorageException.FromResource("TheFileExceedsTheOSSMultipartUploadSizeLimit", "FileTooLarge");
        var initiated = await client.InitiateMultipartUploadAsync(new()
        {
            Bucket = bucket,
            Key = key,
            ForbidOverwrite = !overwrite,
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
        string uploadId = initiated.UploadId ?? throw ObjectStorageException.FromResource("OSSReturnedAnInvalidMultipartUploadResponse", "InvalidResponse");
        bool completed = false;
        try
        {
            var parts = new List<OssModels.UploadPart>();
            long offset = 0;
            for (long partNumber = 1; offset < size; partNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                long length = Math.Min(partSize, size - offset);
                long start = offset;
                using var partBody = new ObjectStorageSliceStream(body, start, length);
                var part = await client.UploadPartAsync(new()
                {
                    Bucket = bucket,
                    Key = key,
                    UploadId = uploadId,
                    PartNumber = partNumber,
                    Body = partBody,
                    ContentLength = length,
                    ProgressFn = (_, transferred, _) => ObjectStorageProgress.Report(progress, start + transferred, size),
                }, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(part.ETag))
                    throw ObjectStorageException.FromResource("OSSReturnedAnInvalidMultipartChecksumResponse", "InvalidResponse");
                parts.Add(new() { PartNumber = partNumber, ETag = part.ETag });
                offset += length;
            }
            cancellationToken.ThrowIfCancellationRequested();
            await client.CompleteMultipartUploadAsync(new()
            {
                Bucket = bucket,
                Key = key,
                UploadId = uploadId,
                ForbidOverwrite = !overwrite,
                CompleteMultipartUpload = new() { Parts = parts },
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                try
                {
                    await client.AbortMultipartUploadAsync(new() { Bucket = bucket, Key = key, UploadId = uploadId },
                        cancellationToken: cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception) { }
            }
        }
    }
}
