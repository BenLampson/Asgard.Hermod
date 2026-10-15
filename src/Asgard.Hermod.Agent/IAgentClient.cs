namespace Asgard.Hermod.Agent;

/// <summary>独立于 WPF、数据库协议和持久化的模型传输边界。</summary>
public interface IAgentClient
{
    /// <summary>使用当前配置请求完整文本回复；取消不转换为业务错误。</summary>
    Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default);
}
