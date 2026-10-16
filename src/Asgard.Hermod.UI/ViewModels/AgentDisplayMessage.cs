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
        OriginalContent = content; Content = content; IsUser = isUser; IsTool = isTool; IsError = isError; RecoveryPrompt = recoveryPrompt; ModelIdentity = modelIdentity;
        Approval = approval;
        if (approval is not null) approval.PropertyChanged += (_, args) => { if (args.PropertyName == nameof(AgentApprovalRequest.IsPending)) OnPropertyChanged(nameof(IsAwaitingApproval)); };
        if (isTool) _elapsed = new();
    }
    /// <summary>用户、模型和工具原文不翻译。</summary>
    public string Content { get; protected set => SetProperty(ref field, value); }
    /// <summary>完整原文，不因建议代码被移入独立动作卡而丢失围栏或命令。</summary>
    public string OriginalContent { get; protected set => SetProperty(ref field, value); }
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
    /// <summary>生成本条回复时接受的原始问题，只允许在创建消息时指定。</summary>
    public string Question { get; init; } = "";
    /// <summary>反馈只保留在当前内存消息，归档切换仍复用同一对象，不向服务器发送。</summary>
    public AgentMessageFeedback Feedback { get; set => SetProperty(ref field, value); }
    /// <summary>生成时的模型协议身份；后续选择不会重写它。</summary>
    public string ModelIdentity { get; }
    /// <summary>实际消息创建时间，不用示例时间填充。</summary>
    public DateTimeOffset CreatedAt { get; } = DateTimeOffset.Now;
    /// <summary>用户消息身份与时间使用完整翻译模板。</summary>
    public string TimestampLabel => Text.Format("ChatUserTime", Role, CreatedAt.LocalDateTime);
    /// <summary>工具折叠状态保留在消息对象上。</summary>
    public bool IsExpanded { get; set => SetProperty(ref field, value); }
    /// <summary>连续工具摘要的展开状态独立于每一步证据，归档切换时保留。</summary>
    public bool IsActivityGroupExpanded { get; set => SetProperty(ref field, value); }
    /// <summary>展示摘要按稳定种类组合，不解析本地化标题。</summary>
    public string ActivityKind => _statusKey is "SqlAgentQueryRunning" or "SqlAgentQueryCompleted" or "SqlAgentQueryStopped" ? "query" : "action";
    /// <summary>已完成步骤的真实耗时供摘要合计，运行中不显示完成耗时。</summary>
    public TimeSpan? ActivityElapsed => _elapsed is { IsRunning: false, ElapsedTicks: > 0 } elapsed ? elapsed.Elapsed : null;
    /// <summary>待确认操作必须保持展开。</summary>
    public bool IsAwaitingApproval => Approval?.IsPending == true || _statusKey is "SshAgentAwaitingApproval" or "ChatAwaitingApproval";
    /// <summary>本条工具步骤的单次确认，历史恢复不会重建授权。</summary>
    public AgentApprovalRequest? Approval { get; }
    /// <summary>只读数据库工具的冻结 SQL、来源和结果阅读状态；普通回复与批准卡为空。</summary>
    public DatabaseAgentEvidenceViewModel? QueryEvidence { get; init; }
    /// <summary>显示角色由稳定标记决定。</summary>
    public virtual string Role => Text.Get(IsTool ? "ChatToolActivity" : IsUser ? "ChatUserRole" : "AgentPanelTitle");
    /// <summary>资源化的当前状态。</summary>
    public string Status => _statusKey.Length == 0 ? "" : Text.Get(_statusKey);
    /// <summary>折叠标题只展示简短动作，完整状态放在提示中，不挤占耗时和展开入口。</summary>
    public string ActivityTitle => _statusKey switch
    {
        "SqlAgentQueryRunning" => Text.Get("ChatQueryRunning"),
        "SqlAgentQueryCompleted" => Text.Get("ChatQueryCompleted"),
        "SshAgentExecuting" => Text.Get("ChatCommandRunning"),
        "SshAgentStepCompleted" => Text.Get("ChatCommandCompleted"),
        "ChatActionExecuting" => Text.Get("ChatCommandRunning"),
        "ChatActionCompleted" => Text.Get("ChatActionCompleted"),
        _ => Role,
    };
    /// <summary>图标和色调只由稳定状态键决定，不比较翻译后的说明。</summary>
    public string ActivityTone => _statusKey switch
    {
        "SqlAgentQueryCompleted" or "SshAgentStepCompleted" or "ChatActionCompleted" => "success",
        "SqlAgentQueryRunning" or "SshAgentExecuting" or "ChatActionExecuting" => "running",
        "SqlAgentQueryStopped" or "SshAgentActionDenied" or "SshAgentOutcomeUnknown" or "SshAgentNotExecuted" or "ChatActionUnknown" or "ChatActionNotExecuted" or "ChatActionFailed" => "warning",
        _ => "muted",
    };
    /// <summary>待审阅、运行中和完成状态使用不同图标。</summary>
    public string ActivityIcon => IsAwaitingApproval ? "shield-alert" : ActivityTone switch { "success" => "circle-check", "running" => "loader-circle", "warning" => "circle-alert", _ => "terminal" };
    /// <summary>仅工具完成后展示实际经过的时间。</summary>
    public string Elapsed => _elapsed is { IsRunning: false, ElapsedTicks: > 0 } elapsed ? Text.Format("ChatActivityElapsed", elapsed.Elapsed.TotalMilliseconds) : "";
    /// <summary>更新原对象，不改变用户已经选择的展开状态。</summary>
    protected void UpdateMessage(string content, string statusKey)
    {
        OriginalContent = content; Content = content; _statusKey = statusKey;
        if (statusKey is "SqlAgentQueryRunning" or "SshAgentExecuting" or "ChatActionExecuting") _elapsed?.Start();
        if (statusKey is not ("SqlAgentQueryRunning" or "SshAgentAwaitingApproval" or "SshAgentExecuting" or "ChatAwaitingApproval" or "ChatActionExecuting")) _elapsed?.Stop();
        OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(IsAwaitingApproval)); OnPropertyChanged(nameof(Elapsed)); OnPropertyChanged(nameof(ActivityTitle)); OnPropertyChanged(nameof(ActivityTone)); OnPropertyChanged(nameof(ActivityIcon)); OnPropertyChanged(nameof(ActivityKind)); OnPropertyChanged(nameof(ActivityElapsed));
    }
}
