using Polly.Timeout;
using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.Infrastructure.Storage;

/// <summary>只消费 SDK 错误码和状态，将完整 SDK 异常留在资源边界内。</summary>
internal static class AliyunOssErrors
{
    /// <summary>SDK 可能包装网络和取消异常，统一展开识别后返回安全业务错误。</summary>
    public static async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (ObjectStorageException) { throw; }
        catch (Exception error)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw Translate(error);
        }
    }

    /// <summary>HEAD 的空正文 404 也表示不存在，403 永远不能当作不存在。</summary>
    public static bool IsMissingObject(Exception error)
    {
        Oss.ServiceException? service = Find<Oss.ServiceException>(error);
        return service is { StatusCode: 404 } && service.ErrorCode is "" or "NoSuchKey" or "NoSuchObject";
    }

    /// <summary>只有明确的 ACL 授权拒绝可以降级；签名或凭据失败仍需反馈给用户。</summary>
    public static bool IsAccessDenied(Exception error)
    {
        Oss.ServiceException? service = Find<Oss.ServiceException>(error);
        return service is { StatusCode: 403 } && service.ErrorCode is null or "" or "AccessDenied";
    }

    /// <summary>安全错误不保留 inner exception，错误消息和错误码都采用固定白名单。</summary>
    public static ObjectStorageException Translate(Exception error)
    {
        Oss.ServiceException? service = Find<Oss.ServiceException>(error);
        return service?.ErrorCode switch
        {
            "InvalidAccessKeyId" or "SignatureDoesNotMatch" => new("身份验证失败，请编辑实例连接凭据。", "AuthenticationFailed"),
            "AccessDenied" => new("权限不足，请确认当前凭据具有此对象操作权限。", "AccessDenied"),
            "FileAlreadyExists" or "ObjectAlreadyExists" => new("同名对象已经存在，请确认覆盖或跳过。", "FileAlreadyExists"),
            "NoSuchBucket" => new("容器已不存在，请刷新容器列表。", "NoSuchBucket"),
            "NoSuchKey" or "NoSuchObject" => new("对象已不存在，请刷新对象列表。", "NoSuchKey"),
            "InvalidObjectState" => new("对象处于归档状态，请先在服务商控制台解冻。", "InvalidObjectState"),
            "RequestTimeTooSkewed" => new("系统时间与 OSS 服务不一致，请校准设备时间。", "ClockSkew"),
            "PreconditionFailed" => new("对象已发生变化，请刷新后重新操作。", "PreconditionFailed"),
            _ when service?.StatusCode is 401 or 403 => new("权限不足，请检查实例凭据与访问策略。", "AccessDenied"),
            _ when service?.StatusCode == 404 => new("请求的资源已不存在，请刷新列表。", "ResourceNotFound"),
            _ when service?.StatusCode == 429 || service?.StatusCode >= 500 => new("OSS 暂时不可用，请稍后重试。", "ServiceUnavailable"),
            _ when Find<Oss.RequestTimeoutException>(error) is not null || Find<TimeoutRejectedException>(error) is not null ||
                Find<OperationCanceledException>(error) is not null => new("对象操作超时，请检查网络后重试。", "RequestTimeout"),
            _ => new("对象存储操作失败，请检查网络、实例配置与操作权限。", "StorageOperationFailed"),
        };
    }

    /// <summary>只在适配器内部检查异常类型，不使用消息、请求地址或 SDK 诊断文本。</summary>
    private static T? Find<T>(Exception? error) where T : Exception
    {
        for (int depth = 0; error is not null && depth < 16; depth++, error = error.InnerException)
            if (error is T matching) return matching;
        return null;
    }
}
