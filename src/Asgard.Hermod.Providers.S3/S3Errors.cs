using System.Net;
using Amazon.S3;
using Polly.Timeout;

namespace Asgard.Hermod.Providers.S3;

/// <summary>只将 SDK 状态和白名单错误码转换为共享资源，不保留原始响应及内部异常。</summary>
internal static class S3Errors
{
    /// <summary>统一保留调用方取消语义，并在提供商边界移除 SDK 诊断详情。</summary>
    internal static async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return await action().ConfigureAwait(false); }
        catch (ObjectStorageException) { throw; }
        catch (Exception error)
        {
            token.ThrowIfCancellationRequested();
            throw Translate(error);
        }
    }

    /// <summary>HEAD 404 只代表对象缺失，不将 403 或明确缺失的桶当作对象不存在。</summary>
    internal static bool Missing(Exception error) => error is AmazonS3Exception
    {
        StatusCode: HttpStatusCode.NotFound, ErrorCode: null or "" or "NotFound" or "NoSuchKey" or "NoSuchObject",
    };

    /// <summary>只用固定错误码构造异常，避免在界面或日志输出凭据和请求地址。</summary>
    internal static ObjectStorageException Translate(Exception error)
    {
        if (error is DeleteObjectsException)
            return Error("StorageDeletePartialFailure", "PartialDeleteFailure");
        var service = error as AmazonS3Exception;
        return service?.ErrorCode switch
        {
            "InvalidAccessKeyId" or "InvalidToken" or "ExpiredToken" or "SignatureDoesNotMatch" => Error("AuthenticationFailedEditTheInstanceConnectionCredentials", "AuthenticationFailed"),
            "AccessDenied" => Error("InsufficientPermissionsCheckThatTheCurrentCredentialsPermitThisObjectOperation", "AccessDenied"),
            "PreconditionFailed" or "FileAlreadyExists" or "ObjectAlreadyExists" => Error("AnObjectWithThisNameAlreadyExistsConfirmOverwriteOrSkip", "FileAlreadyExists"),
            "ConditionalRequestConflict" => Error("TheObjectHasChangedRefreshBeforeTryingAgain", "PreconditionFailed"),
            "NoSuchBucket" => Error("TheBucketNoLongerExistsRefreshTheBucketList", "NoSuchBucket"),
            "NoSuchKey" or "NoSuchObject" => Error("TheObjectNoLongerExistsRefreshTheObjectList", "NoSuchKey"),
            "InvalidObjectState" => Error("TheObjectIsArchivedRestoreItInTheProviderConsoleFirst", "InvalidObjectState"),
            "RequestTimeTooSkewed" or "RequestExpired" => Error("StorageClockSkew", "ClockSkew"),
            "NotImplemented" => Error("StorageOperationUnsupported", "NotSupported"),
            _ when service?.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => Error("InsufficientPermissionsCheckTheInstanceCredentialsAndAccessPolicy", "AccessDenied"),
            _ when service?.StatusCode == HttpStatusCode.PreconditionFailed => Error("AnObjectWithThisNameAlreadyExistsConfirmOverwriteOrSkip", "FileAlreadyExists"),
            _ when service?.StatusCode == HttpStatusCode.NotFound => Error("TheRequestedResourceNoLongerExistsRefreshTheList", "ResourceNotFound"),
            _ when (int?)service?.StatusCode >= 500 || (int?)service?.StatusCode == 429 => Error("StorageServiceUnavailable", "ServiceUnavailable"),
            _ when error is OperationCanceledException or TimeoutRejectedException || service?.ErrorCode == "RequestTimeout" => Error("TheObjectOperationTimedOutCheckYourNetworkAndTryAgain", "RequestTimeout"),
            _ => Error("ObjectStorageOperationFailedCheckTheNetworkInstanceConfigurationAndOperationPermissio", "StorageOperationFailed"),
        };
    }

    /// <summary>无效协议响应使用固定提示。</summary>
    internal static ObjectStorageException InvalidResponse() => Error("StorageInvalidResponse", "InvalidResponse");
    private static ObjectStorageException Error(string resource, string code) => ObjectStorageException.FromResource(resource, code);
}
