namespace Asgard.Hermod.ObjectStorage.Abstractions.Services;

/// <summary>对象存储适配器返回的安全业务错误；禁止传入原始 SDK 消息或内部异常。</summary>
public sealed class ObjectStorageException(string message, string? errorCode = null) : Exception(message)
{
    /// <summary>适配器白名单内的服务错误码，不包含原始响应正文。</summary>
    public string? ErrorCode { get; } = errorCode;

    /// <summary>以稳定文案键创建用户可见错误，服务码始终保留原始协议值。</summary>
    public static ObjectStorageException FromResource(string resourceKey, string? errorCode = null,
        params object?[] arguments)
    {
        var exception = new ObjectStorageException(Text.Format(resourceKey, arguments), errorCode);
        exception.Data["Hermod.ResourceKey"] = resourceKey;
        exception.Data["Hermod.ResourceArguments"] = arguments;
        return exception;
    }
}
