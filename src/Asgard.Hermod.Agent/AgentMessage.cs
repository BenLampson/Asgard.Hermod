namespace Asgard.Hermod.Agent;

/// <summary>纯文本对话消息，不携带连接对象、凭据或可执行委托。</summary>
public sealed record AgentMessage(AgentRole Role, string Content);
