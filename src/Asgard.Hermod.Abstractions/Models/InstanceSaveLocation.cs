namespace Asgard.Hermod.Abstractions.Models;

/// <summary>实例配置的保存范围，登录不会自动迁移本地数据。</summary>
public enum InstanceSaveLocation
{
    /// <summary>当前设备上的用户级数据。</summary>
    Local,
    /// <summary>未来由账户同步服务提供的空间。</summary>
    Account,
}
