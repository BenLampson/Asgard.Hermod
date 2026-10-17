namespace Asgard.Hermod.Agent;

/// <summary>文本与工具协议消息，不携带连接对象、凭据或可执行委托。</summary>
public sealed record AgentMessage(AgentRole Role, string Content)
{
    /// <summary>助手声明的工具调用，只有显式启用工具的会话才能处理。</summary>
    public IReadOnlyList<AgentToolCall> ToolCalls { get; init; } = [];
    /// <summary>工具结果关联的调用标识，不参与显示或权限判断。</summary>
    public string? ToolCallId { get; init; }
}
