namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>每条请求保留当次分析范围，后续日志刷新不会重写证据说明。</summary>
public sealed class SshLogAnalysisMessage(string content, bool isUser, string context, bool isError = false, string recoveryPrompt = "", string modelIdentity = "", SshLogAttachment? attachment = null) : AgentDisplayMessage(content, isUser, isError: isError, recoveryPrompt: recoveryPrompt, modelIdentity: modelIdentity)
{
    /// <summary>发送时冻结的上下文摘要。</summary>
    public string Context { get; } = context;
    /// <summary>本条消息实际使用的不可变证据。</summary>
    public SshLogAttachment? Attachment { get; } = attachment;
    /// <summary>角色仅用于显示，不参与业务判断。</summary>
    public override string Role => Text.Get(IsUser ? "SshAgentYou" : "SshLogAiTitle");
}
