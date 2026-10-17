namespace Asgard.Hermod.Abstractions.Services;

/// <summary>当前工作区归属与账户状态均由本地持久化实现。</summary>
public interface IConfigurationSyncStateStore
{
    /// <summary>当前本机工作区的稳定归属，空字符串代表未登录工作区。</summary>
    string ActiveOwner { get; set; }
    /// <summary>读取当前用户保护的账户状态。</summary>
    ConfigurationSyncState Load(string owner);
    /// <summary>原子保存加密状态。</summary>
    void Save(string owner, ConfigurationSyncState state);
}
