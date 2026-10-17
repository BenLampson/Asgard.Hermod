namespace Asgard.Hermod.Abstractions.Localization;

/// <summary>业务异常向展示边界传递稳定文案键，不暴露内部异常文本。</summary>
public interface ILocalizedError
{
    /// <summary>共享文案资源中的键。</summary>
    string ResourceKey { get; }
    /// <summary>仅允许用户可见的业务参数，不传递凭据或原始响应。</summary>
    object?[] ResourceArguments { get; }
}
