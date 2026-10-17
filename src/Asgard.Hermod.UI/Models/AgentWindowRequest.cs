namespace Asgard.Hermod.UI.Models;

/// <summary>独立助手固定绑定一个来源；窗口复用以来源模型引用为准，不追踪当前选中项。</summary>
public sealed record AgentWindowRequest(object Source, object Conversation, string Title, string Detail, string Icon,
    Func<FrameworkElement> CreateContent, Action Locate, Func<bool> IsClosed);
