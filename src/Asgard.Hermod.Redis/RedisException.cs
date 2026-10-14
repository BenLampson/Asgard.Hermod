using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Redis;

/// <summary>只传递稳定错误码与资源键，不输出 SDK 响应、键值或凭据。</summary>
public sealed class RedisException(string resourceKey, string code) : Exception(Text.Get(resourceKey)), ILocalizedError
{
    /// <inheritdoc />
    public string ResourceKey { get; } = resourceKey;
    /// <inheritdoc />
    public object?[] ResourceArguments => [];
    /// <summary>用于诊断的稳定代码。</summary>
    public string Code { get; } = code;
    /// <summary>只检查协议错误前缀，不将原始响应交给界面。</summary>
    public static RedisException Safe(Exception error, bool dispatchedWrite = false)
    {
        if (error is RedisException safe) return safe;
        if (error is RedisServerException server)
        {
            if (server.Message.StartsWith("NOAUTH", StringComparison.Ordinal) || server.Message.StartsWith("WRONGPASS", StringComparison.Ordinal))
                return new("RedisConnectionFailed", "REDIS_AUTH");
            if (server.Message.StartsWith("NOPERM", StringComparison.Ordinal))
                return new("RedisPermissionDenied", "REDIS_PERMISSION");
            if (server.Message.StartsWith("WRONGTYPE", StringComparison.Ordinal)) return new("RedisConflict", "REDIS_CONFLICT");
            return new("RedisCommandFailed", "REDIS_COMMAND");
        }
        return dispatchedWrite ? new("RedisWriteUnknown", "REDIS_WRITE_UNKNOWN") : new("RedisConnectionFailed", "REDIS_CONNECTION");
    }
}
