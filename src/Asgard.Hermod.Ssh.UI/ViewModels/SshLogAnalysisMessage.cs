namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>每条请求保留当次分析范围，后续日志刷新不会重写证据说明。</summary>
public sealed record SshLogAnalysisMessage(string Content, bool IsUser, string Context)
{
    /// <summary>只有模型回复启用 Markdown，用户原文保持字面显示。</summary>
    public bool IsAssistant => !IsUser;
    /// <summary>角色仅用于显示，不参与业务判断。</summary>
    public string Role => Text.Get(IsUser ? "SshAgentYou" : "SshLogAiTitle");
}
