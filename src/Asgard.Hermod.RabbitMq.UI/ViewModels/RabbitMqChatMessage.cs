namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

/// <summary>共享流式正文与思考阅读状态。</summary>
public sealed class RabbitMqChatMessage(string content, bool isUser, string modelIdentity = "") : Asgard.Hermod.UI.ViewModels.AgentDisplayMessage(content, isUser, modelIdentity: modelIdentity);
