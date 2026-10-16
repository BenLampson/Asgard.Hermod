namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>只记录真实完成的模型回复。</summary>
public sealed record RabbitMqChatMessage(string Content, bool IsUser);
