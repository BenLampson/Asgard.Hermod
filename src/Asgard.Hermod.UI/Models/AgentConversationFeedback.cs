namespace Asgard.Hermod.UI.Models;

/// <summary>每页对话独立保存的安全反馈与冻结附件，避免新对话继承上一页错误。</summary>
public sealed record AgentConversationFeedback(string Error = "", string FailedPrompt = "", object? Attachment = null);
