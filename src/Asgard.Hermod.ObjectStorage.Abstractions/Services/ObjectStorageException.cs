namespace Asgard.Hermod.ObjectStorage.Abstractions.Services;

/// <summary>对象存储适配器返回的安全业务错误；禁止传入原始 SDK 消息或内部异常。</summary>
public sealed class ObjectStorageException(string message, string? errorCode = null) : Exception(message)
{
    /// <summary>适配器白名单内的服务错误码，不包含原始响应正文。</summary>
    public string? ErrorCode { get; } = errorCode;
}
