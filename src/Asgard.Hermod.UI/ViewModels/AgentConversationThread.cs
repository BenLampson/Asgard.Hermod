using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>同一来源内的内存对话页，保留消息对象上的折叠状态与草稿。</summary>
public sealed class AgentConversationThread : ObservableObject
{
    /// <summary>稳定的内存对话标识。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    /// <summary>标题取首条用户原文，空白对话使用资源。</summary>
    public string Title { get; internal set => SetProperty(ref field, value); } = Text.Get("AgentNewConversation");
    internal object[] Messages { get; set; } = [];
    internal string Draft { get; set; } = "";
    internal AgentConversationSnapshot EngineState { get; set; } = AgentConversationSnapshot.Empty;
    internal object? ContextState { get; set; }
}
