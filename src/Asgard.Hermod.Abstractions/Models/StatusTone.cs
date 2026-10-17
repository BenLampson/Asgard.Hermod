namespace Asgard.Hermod.Abstractions.Models;

/// <summary>描述界面状态的语义色，不承载连接协议状态。</summary>
public enum StatusTone
{
    /// <summary>正常或成功。</summary>
    Success,
    /// <summary>需要关注。</summary>
    Warning,
    /// <summary>错误或不可用。</summary>
    Danger,
    /// <summary>未配置或中性状态。</summary>
    Muted,
    /// <summary>连接中或正在执行的强调状态。</summary>
    Accent,
}
