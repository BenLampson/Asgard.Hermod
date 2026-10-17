namespace Asgard.Hermod.Agent;

/// <summary>独立于 WPF、数据库协议和持久化的模型传输边界。</summary>
public interface IAgentClient
{
    /// <summary>逐步接收模型响应；旧客户端保留完整返回行为，回调完成后才继续读取。</summary>
    Task<AgentReply> CompleteStreamingAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition>? tools,
        Func<AgentStreamUpdate, CancellationToken, ValueTask>? receive, CancellationToken cancellationToken = default)
        => tools is null ? CompleteAsync(messages, cancellationToken) : CompleteWithToolsAsync(messages, tools, cancellationToken);
    /// <summary>冻结一次完整任务的配置；替代客户端默认保持其原有行为。</summary>
    IAgentClient ForOptions(AgentModelOptions options, string profileName) => this;
    /// <summary>使用当前配置请求完整文本回复；取消不转换为业务错误。</summary>
    Task<AgentReply> CompleteAsync(IReadOnlyList<AgentMessage> messages, CancellationToken cancellationToken = default);
    /// <summary>显式提供工具定义；不支持工具的客户端仍可返回文本建议，但不得自行执行。</summary>
    Task<AgentReply> CompleteWithToolsAsync(IReadOnlyList<AgentMessage> messages, IReadOnlyList<AgentToolDefinition> tools, CancellationToken cancellationToken = default)
        => CompleteAsync(messages, cancellationToken);
}
