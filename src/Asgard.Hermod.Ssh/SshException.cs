using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Ssh;

/// <summary>协议边界仅传播稳定错误码与资源键，原始 SDK 响应不进入 UI。</summary>
public sealed class SshException(string resourceKey, string code) : Exception(Text.Get(resourceKey)), ILocalizedError
{
    /// <inheritdoc />
    public string ResourceKey { get; } = resourceKey;
    /// <inheritdoc />
    public object?[] ResourceArguments => [];
    /// <summary>稳定诊断码，不含服务器响应或凭据。</summary>
    public string Code { get; } = code;
}
