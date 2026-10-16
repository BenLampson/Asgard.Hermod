namespace Asgard.Hermod.Agent;

/// <summary>仅成功轮次的内存快照；不包含临时数据库元数据、日志附件或凭据。</summary>
public sealed record AgentConversationSnapshot(IReadOnlyList<AgentMessage> Messages, string ScopeId = "")
{
    /// <summary>新对话的空上下文。</summary>
    public static AgentConversationSnapshot Empty { get; } = new(Array.Empty<AgentMessage>());
    /// <summary>恢复只接受完整用户/助手轮次，不允许把工具或系统指令提升到历史。</summary>
    internal void Validate(int maximumMessages, int maximumCharacters)
    {
        if (Messages is null || Messages.Count % 2 != 0 || Messages.Count > maximumMessages || ScopeId is null || ScopeId.Length > 8192)
            throw Text.Exception("AgentInvalidHistory");
        long characters = 0;
        for (int index = 0; index < Messages.Count; index++)
        {
            var message = Messages[index];
            if (message is null || message.Content is null || message.Role != (index % 2 == 0 ? AgentRole.User : AgentRole.Assistant) || message.ToolCalls is null || message.ToolCalls.Count != 0 || message.ToolCallId is not null)
                throw Text.Exception("AgentInvalidHistory");
            characters += message.Content.Length;
        }
        if (characters > maximumCharacters) throw Text.Exception("AgentInputTooLarge");
    }
}
