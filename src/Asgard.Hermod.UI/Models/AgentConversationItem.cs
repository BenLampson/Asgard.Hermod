namespace Asgard.Hermod.UI.Models;

/// <summary>展示投影包裹原对象；既不替换业务消息，也不改变归档和单次批准的所有权。</summary>
public sealed record AgentConversationItem(object Message, bool IsGroup = false);
