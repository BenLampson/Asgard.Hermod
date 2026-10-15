namespace Asgard.Hermod.Agent;

/// <summary>模型消息协议角色，业务逻辑不依赖本地化显示文本。</summary>
public enum AgentRole
{
    /// <summary>应用定义的固定规则。</summary>
    System,
    /// <summary>用户请求及明确选择的上下文。</summary>
    User,
    /// <summary>模型生成的文本回复。</summary>
    Assistant,
}
