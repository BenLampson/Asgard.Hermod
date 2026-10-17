namespace Asgard.Hermod.RabbitMq;

/// <summary>只携带稳定资源键和结果状态，不保留响应正文、认证头或原始异常。</summary>
public sealed class RabbitMqFailure(string resourceKey, bool outcomeUnknown = false, bool connectionLost = false)
    : Exception(resourceKey), ILocalizedError
{
    /// <summary>供界面安全翻译的错误键。</summary>
    public string ResourceKey { get; } = resourceKey;
    /// <summary>错误不包含来自服务器的任意字符串。</summary>
    public object?[] ResourceArguments => [];
    /// <summary>请求可能已执行；界面必须禁止自动重放。</summary>
    public bool OutcomeUnknown { get; } = outcomeUnknown;
    /// <summary>暂停自动刷新并提供显式重连。</summary>
    public bool ConnectionLost { get; } = connectionLost;
}
