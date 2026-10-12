using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>探针单独保留版本回执，普通资源上传和删除仍使用既有业务语义。</summary>
public sealed partial class AliyunOssObjectStorageClient
{
    /// <inheritdoc />
    public async Task<ObjectStorageProbeWriteResult> UploadProbeAsync(string bucket, string key, Stream body, long size,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(size);
        AliyunOssEndpoint.ValidateBucket(bucket);
        ThrowIfProbePreflightCancelled(token);
        await EnsureProbeKeyMissingAsync(bucket, key, token).ConfigureAwait(false);
        ThrowIfProbePreflightCancelled(token);
        try
        {
            var result = await client.PutObjectAsync(new()
            {
                Bucket = bucket,
                Key = key,
                Body = body,
                ContentLength = size,
                ForbidOverwrite = true,
            }, cancellationToken: token).ConfigureAwait(false);
            // 暂停版本化容器返回字面字符串 null，必须原样保留，不能转换为缺失参数。
            return new(result.VersionId);
        }
        catch (Exception error)
        {
            // 明确拒绝优先于同时发生的取消，防止调用方把 409 当作未知上传并误删既有对象。
            if (FindProbeServiceError(error) is { StatusCode: >= 400 and < 500 })
                throw TranslateProbeRejection(error, "ProbeWriteRejected");
            token.ThrowIfCancellationRequested();
            throw AliyunOssErrors.Translate(error);
        }
    }

    /// <inheritdoc />
    public Task<ObjectStorageProbeReadResult> OpenProbeReadAsync(string bucket, string key, string? versionId,
        int maxBytes, CancellationToken token) => AliyunOssErrors.ExecuteAsync(async () =>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
            AliyunOssEndpoint.ValidateBucket(bucket);
            var result = await client.GetObjectAsync(new()
            {
                Bucket = bucket,
                Key = key,
                VersionId = versionId,
                Range = $"bytes=0-{maxBytes - 1}",
                RangeBehavior = "standard",
            }, ReadOptions, token).ConfigureAwait(false);
            if (result.Body is null) throw InvalidResponse();
            return new ObjectStorageProbeReadResult(new(result.Body, result.ContentLength ?? -1), result.VersionId);
        }, token);

    /// <inheritdoc />
    public Task DeleteProbeAsync(string bucket, string key, string? versionId, CancellationToken token) =>
        AliyunOssErrors.ExecuteAsync(async () =>
        {
            AliyunOssEndpoint.ValidateBucket(bucket);
            var result = await client.DeleteObjectAsync(new()
            {
                Bucket = bucket,
                Key = key,
                VersionId = versionId,
            }, cancellationToken: token).ConfigureAwait(false);
            // 未指定版本的删除可能只创建标记；这种响应不能证明临时数据已清理。
            if (result.DeleteMarker == true || versionId is not null && result.VersionId is not null &&
                !string.Equals(versionId, result.VersionId, StringComparison.Ordinal))
                throw new ObjectStorageException("OSS 未确认临时对象的精确版本已永久删除，请检查服务商控制台。", "ProbeCleanupIncomplete");
            return true;
        }, token);

    /// <summary>随机键预检仍拒绝已存在的对象；版本化容器忽略禁止覆盖头，预检不可省略。</summary>
    private async Task EnsureProbeKeyMissingAsync(string bucket, string key, CancellationToken token)
    {
        try
        {
            await client.HeadObjectAsync(new() { Bucket = bucket, Key = key }, ReadOptions, token).ConfigureAwait(false);
        }
        catch (Exception error) when (AliyunOssErrors.IsMissingObject(error))
        {
            ThrowIfProbePreflightCancelled(token);
            return;
        }
        catch (Exception)
        {
            ThrowIfProbePreflightCancelled(token);
            throw new ObjectStorageException("临时对象写入前的检查失败，未尝试创建测试对象。", "ProbePreflightFailed");
        }
        throw new ObjectStorageException("随机测试对象已存在，未尝试写入或删除该对象。", "FileAlreadyExists");
    }

    /// <summary>PUT 尚未开始时的取消明确表示没有写入，上层不必用回读猜测或提示未知残留。</summary>
    private static void ThrowIfProbePreflightCancelled(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            throw new ObjectStorageException("临时对象写入前的检查已取消，未尝试创建测试对象。", "ProbePreflightFailed");
    }

    /// <summary>保留调用方可识别的安全拒绝码，其余明确拒绝采用固定文案和状态。</summary>
    private static ObjectStorageException TranslateProbeRejection(Exception error, string fallbackCode)
    {
        ObjectStorageException translated = AliyunOssErrors.Translate(error);
        return translated.ErrorCode is "AccessDenied" or "AuthenticationFailed" or "NoSuchBucket" or
            "FileAlreadyExists" or "PreconditionFailed"
            ? translated
            : new ObjectStorageException("OSS 已拒绝临时对象的写入请求，未创建测试对象。", fallbackCode);
    }

    /// <summary>SDK 包装异常只在适配边界展开，不把第三方请求或诊断内容传给调用方。</summary>
    private static Oss.ServiceException? FindProbeServiceError(Exception? error)
    {
        for (int depth = 0; error is not null && depth < 16; depth++, error = error.InnerException)
            if (error is Oss.ServiceException service) return service;
        return null;
    }
}
