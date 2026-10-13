namespace Asgard.Hermod.Abstractions.Models;

/// <summary>同步状态不依赖翻译文本。</summary>
public enum ConfigurationSyncStatus
{
    /// <summary>未登录，使用本地工作区。</summary>
    Local,
    /// <summary>登录账户尚未在本机解锁同步。</summary>
    PasswordRequired,
    /// <summary>本机更改等待上传。</summary>
    Pending,
    /// <summary>正在检查、合并或上传。</summary>
    Synchronizing,
    /// <summary>最近一次检查和应用已成功。</summary>
    Synced,
    /// <summary>有分歧需用户选择。</summary>
    Conflict,
    /// <summary>失败保留本地内容，等待重试。</summary>
    Failed,
}
