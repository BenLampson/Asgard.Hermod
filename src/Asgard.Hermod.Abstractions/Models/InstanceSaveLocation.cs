namespace Asgard.Hermod.Abstractions.Models;

/// <summary>由当前登录状态决定的配置归属。</summary>
public enum InstanceSaveLocation
{
    /// <summary>当前设备上的用户级数据。</summary>
    Local,
    /// <summary>账户空间，本机保留受保护的离线副本。</summary>
    Account,
}
