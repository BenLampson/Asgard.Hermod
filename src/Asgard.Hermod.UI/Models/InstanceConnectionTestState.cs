namespace Asgard.Hermod.UI.Models;

/// <summary>区分实例向导中的连接测试状态，成功仅对被验证的连接信息有效。</summary>
public enum InstanceConnectionTestState
{
    /// <summary>尚未测试、已取消，或连接信息已经变更。</summary>
    NotStarted,
    /// <summary>服务商正在执行只读连接验证。</summary>
    Running,
    /// <summary>验证未通过，可修改信息或再次测试。</summary>
    Failed,
    /// <summary>当前连接信息已经通过验证。</summary>
    Succeeded,
}
