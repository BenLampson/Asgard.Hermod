using Polly.Timeout;
using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.Providers.AliyunOss;

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
            "InvalidAccessKeyId" or "SignatureDoesNotMatch" => ObjectStorageException.FromResource("AuthenticationFailedEditTheInstanceConnectionCredentials", "AuthenticationFailed"),
            "AccessDenied" => ObjectStorageException.FromResource("InsufficientPermissionsCheckThatTheCurrentCredentialsPermitThisObjectOperation", "AccessDenied"),
            "FileAlreadyExists" or "ObjectAlreadyExists" => ObjectStorageException.FromResource("AnObjectWithThisNameAlreadyExistsConfirmOverwriteOrSkip", "FileAlreadyExists"),
            "NoSuchBucket" => ObjectStorageException.FromResource("TheBucketNoLongerExistsRefreshTheBucketList", "NoSuchBucket"),
            "NoSuchKey" or "NoSuchObject" => ObjectStorageException.FromResource("TheObjectNoLongerExistsRefreshTheObjectList", "NoSuchKey"),
            "InvalidObjectState" => ObjectStorageException.FromResource("TheObjectIsArchivedRestoreItInTheProviderConsoleFirst", "InvalidObjectState"),
            "RequestTimeTooSkewed" => ObjectStorageException.FromResource("TheSystemTimeDiffersFromOSSCorrectTheDeviceTime", "ClockSkew"),
            "PreconditionFailed" => ObjectStorageException.FromResource("TheObjectHasChangedRefreshBeforeTryingAgain", "PreconditionFailed"),
            _ when service?.StatusCode is 401 or 403 => ObjectStorageException.FromResource("InsufficientPermissionsCheckTheInstanceCredentialsAndAccessPolicy", "AccessDenied"),
            _ when service?.StatusCode == 404 => ObjectStorageException.FromResource("TheRequestedResourceNoLongerExistsRefreshTheList", "ResourceNotFound"),
            _ when service?.StatusCode == 429 || service?.StatusCode >= 500 => ObjectStorageException.FromResource("OSSIsTemporarilyUnavailableTryAgainLater", "ServiceUnavailable"),
            _ when Find<Oss.RequestTimeoutException>(error) is not null || Find<TimeoutRejectedException>(error) is not null ||
                Find<OperationCanceledException>(error) is not null => ObjectStorageException.FromResource("TheObjectOperationTimedOutCheckYourNetworkAndTryAgain", "RequestTimeout"),
            _ => ObjectStorageException.FromResource("ObjectStorageOperationFailedCheckTheNetworkInstanceConfigurationAndOperationPermissio", "StorageOperationFailed"),
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
