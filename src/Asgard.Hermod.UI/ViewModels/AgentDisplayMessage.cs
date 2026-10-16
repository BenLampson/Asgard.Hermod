using System.Diagnostics;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>共享消息阅读状态；工具正文默认折叠，更新状态不重建消息对象。</summary>
public class AgentDisplayMessage : ObservableObject, IAgentDisplayMessage
{
    private readonly Stopwatch? _elapsed;
    private string _statusKey = "";
    /// <summary>保存原文、来源和生成时模型身份，不读取当前模型选择。</summary>
    public AgentDisplayMessage(string content, bool isUser, bool isTool = false, bool isError = false, string recoveryPrompt = "", string modelIdentity = "", AgentApprovalRequest? approval = null)
    {
        Content = content; IsUser = isUser; IsTool = isTool; IsError = isError; RecoveryPrompt = recoveryPrompt; ModelIdentity = modelIdentity;
        Approval = approval;
        if (approval is not null) approval.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AgentApprovalRequest.IsPending)) OnPropertyChanged(nameof(IsAwaitingApproval)); };
        if (isTool) _elapsed = new();
    }
    /// <summary>用户、模型和工具原文不翻译。</summary>
    public string Content { get; protected set => SetProperty(ref field, value); }
    /// <summary>稳定的用户来源标记。</summary>
    public bool IsUser { get; }
    /// <summary>工具步骤不能被提取为新的执行建议。</summary>
    public bool IsTool { get; }
    /// <summary>安全错误使用独立消息卡。</summary>
    public bool IsError { get; }
    /// <summary>普通模型回复才解析 Markdown。</summary>
    public bool IsAssistant => !IsUser && !IsTool && !IsError;
    /// <summary>恢复失败问题只填入草稿，不自动发送。</summary>
    public string RecoveryPrompt { get; }
    /// <summary>生成时的模型协议身份；后续选择不会重写它。</summary>
    public string ModelIdentity { get; }
    /// <summary>实际消息创建时间，不用示例时间填充。</summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    /// <summary>用户消息身份与时间使用完整翻译模板。</summary>
    public string TimestampLabel => Text.Format("ChatUserTime", Role, CreatedAt.LocalDateTime);
    /// <summary>工具折叠状态保留在消息对象上。</summary>
    public bool IsExpanded { get; set => SetProperty(ref field, value); }
    /// <summary>待确认操作必须保持展开。</summary>
    public bool IsAwaitingApproval => Approval?.IsPending == true || _statusKey is "SshAgentAwaitingApproval" or "ChatAwaitingApproval";
    /// <summary>本条工具步骤的单次确认，历史恢复不会重建授权。</summary>
    public AgentApprovalRequest? Approval { get; }
    /// <summary>显示角色由稳定标记决定。</summary>
    public virtual string Role => Text.Get(IsTool ? "ChatToolActivity" : IsUser ? "ChatUserRole" : "AgentPanelTitle");
    /// <summary>资源化的当前状态。</summary>
    public string Status => _statusKey.Length == 0 ? "" : Text.Get(_statusKey);
    /// <summary>仅工具完成后展示实际经过的时间。</summary>
    public string Elapsed => _elapsed is { IsRunning: false, ElapsedTicks: > 0 } elapsed ? Text.Format("ChatActivityElapsed", elapsed.Elapsed.TotalMilliseconds) : "";
    /// <summary>更新原对象，不改变用户已经选择的展开状态。</summary>
    protected void UpdateMessage(string content, string statusKey)
    {
        Content = content; _statusKey = statusKey;
        if (statusKey is "SqlAgentQueryRunning" or "SshAgentExecuting" or "ChatActionExecuting") _elapsed?.Start();
        if (statusKey is not ("SqlAgentQueryRunning" or "SshAgentAwaitingApproval" or "SshAgentExecuting" or "ChatAwaitingApproval" or "ChatActionExecuting")) _elapsed?.Stop();
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsAwaitingApproval)); OnPropertyChanged(nameof(Elapsed));
    }
}
