namespace Asgard.Hermod.Abstractions.Services;

/// <summary>按当前工作区保存模型集合；实现必须使用当前设备用户的凭据保护，不能保存明文密钥。</summary>
public interface IAgentModelStore
{
    /// <summary>未初始化返回 null；解密失败必须报告安全错误，不能当作空集合覆盖。</summary>
    AgentModelSet? Load();
    /// <summary>原子保存完整集合；空集合明确删除全部配置，不能复活旧偏好。</summary>
    void Save(AgentModelSet models);
    /// <summary>本机提交、云端恢复或账号切换完成后通知，不在事务中触发。</summary>
    event EventHandler? Changed;
}
