namespace Asgard.Hermod.Server;

/// <summary>业务错误只携带 HTTP 状态和共享资源键，不暴露数据库或协议响应。</summary>
public sealed class HermodApiException(int statusCode, string resourceKey) : Exception(resourceKey)
{
    /// <summary>确定的业务状态码。</summary>
    public int StatusCode { get; } = statusCode;
    /// <summary>稳定的本地化资源标识。</summary>
    public string ResourceKey { get; } = resourceKey;
}
