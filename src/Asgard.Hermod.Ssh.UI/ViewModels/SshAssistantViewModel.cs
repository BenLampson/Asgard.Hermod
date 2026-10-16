using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>当前主机的排障助手；隐藏分屏保留任务和草稿，断线或重连立即撤销旧任务权限。</summary>
public sealed class SshAssistantViewModel : ObservableObject, IDisposable
{
    private readonly SshAgentConversation? _conversation;
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly Func<string> _host;
    private readonly ISshWorkspaceDialogs _dialogs;
    private readonly bool _useInlineApproval;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private bool _sourceClosed;
    private long _revision;

    /// <summary>复用模型和已认证的 SSH 连接；连接凭据从不进入模型上下文。</summary>
    public SshAssistantViewModel(IAgentClient? client, Func<ISshWorkspaceConnection?> connection, Func<string> host, ISshWorkspaceDialogs dialogs, bool useInlineApproval = false)
    {
        _conversation = client is null ? null : new(client); _connection = connection; _host = host; _dialogs = dialogs;
        _useInlineApproval = useInlineApproval;
        Archive = new(Messages, () => _conversation?.Capture() ?? AgentConversationSnapshot.Empty, snapshot => _conversation?.Restore(snapshot), () => Prompt, value => Prompt = value, () => IsBusy,
            () => new Asgard.Hermod.UI.Models.AgentConversationFeedback(Error, LastFailedPrompt), state =>
            {
                var saved = state as Asgard.Hermod.UI.Models.AgentConversationFeedback ?? new();
                Error = saved.Error; LastFailedPrompt = saved.FailedPrompt;
            });
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsSourceClosed && !IsBusy && _connection() is not null && !string.IsNullOrWhiteSpace(Prompt));
        RestoreDraftCommand = new RelayCommand(value => Prompt = value as string ?? LastFailedPrompt, value => !IsSourceClosed && !IsBusy && Prompt.Length == 0 && (value is string { Length: > 0 } || LastFailedPrompt.Length > 0));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        ClearCommand = new RelayCommand(_ => { _conversation?.Clear(); Messages.Clear(); Error = ""; }, _ => !IsBusy && !IsSourceClosed);
        InsertCommand = new RelayCommand(value => { if (value is SshAssistantCommand item && IsCurrent(item)) InsertRequested?.Invoke(this, item.Command); }, value => value is SshAssistantCommand item && IsCurrent(item) && !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(value => value is SshAssistantCommand item ? ExecuteAsync(item) : Task.CompletedTask, value => value is SshAssistantCommand item && IsCurrent(item) && !IsBusy);
    }

    /// <summary>绑定原控制台的模型选择。</summary>
    public AgentModelSession? ModelSession => _conversation?.ModelSession;
    /// <summary>用户明确填入终端，不添加回车。</summary>
    public event EventHandler<string>? InsertRequested;
    /// <summary>快捷提问只填入草稿，仍需用户发送。</summary>
    public IReadOnlyList<string> QuickPrompts { get; } = [Text.Get("ChatSshPromptOne"), Text.Get("ChatSshPromptTwo"), Text.Get("ChatSshPromptThree")];
    /// <summary>有界显示历史，工具步骤也计入上限。</summary>
    public ObservableCollection<SshAssistantMessage> Messages { get; } = [];
    /// <summary>当前控制台的独立内存对话历史。</summary>
    public AgentConversationArchive Archive { get; }
    /// <summary>Shell 已结束或来源已关闭时停用新操作。</summary>
    public bool IsSourceClosed => _disposed || _sourceClosed;
    /// <summary>失败问题可恢复但不会盲目重试远端操作。</summary>
    public string LastFailedPrompt { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>只恢复到空草稿，不覆盖下一条输入。</summary>
    public RelayCommand RestoreDraftCommand { get; }
    /// <summary>收起后保留的草稿；发送期间新编辑的内容不会被清空。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>用户明确附加的终端选区，最多八千字符。</summary>
    public string SelectedOutput { get; set { if (SetProperty(ref field, value.Length <= 8192 ? value : value[..8192])) OnPropertyChanged(nameof(ContextLabel)); } } = "";
    /// <summary>发送选区仍需明确选择，不自动读取终端历史。</summary>
    public bool IncludeOutput { get; set => SetProperty(ref field, value); }
    /// <summary>本次选择的上下文内容与长度可见。</summary>
    public string ContextLabel => Text.Format("SshAgentContext", _host(), SelectedOutput.Length);
    /// <summary>模型、确认和命令全程串行，防止模态确认期间跨入口重入。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>思考、诊断、等待确认等阶段通过资源显示。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>当前控制台单次操作的可取消确认，不授予后续工具权限。</summary>
    public AgentApprovalRequest? PendingApproval { get; private set => SetProperty(ref field, value); }
    /// <summary>安全错误资源。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>发送排障任务。</summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>停止当前模型请求和远端操作，不声称撤销已经生效的修改。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>新对话。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>仅填入终端。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>确认后执行建议，再将结果回传模型。</summary>
    public AsyncRelayCommand ExecuteCommand { get; }

    private bool IsCurrent(SshAssistantCommand item) => !IsSourceClosed && item.Revision == _revision && _connection() is not null;
    private void EnsureCurrent(long revision, ISshWorkspaceConnection connection, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || revision != _revision || !ReferenceEquals(connection, _connection())) throw Text.Exception("SshWorkspaceUnavailable");
    }

    private async Task SendAsync()
    {
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt;
        await RunTaskAsync(prompt, null, () => { if (Prompt == prompt) Prompt = ""; });
    }

    private Task ExecuteAsync(SshAssistantCommand item)
    {
        if (!IsCurrent(item)) return Task.CompletedTask;
        if (string.IsNullOrWhiteSpace(item.Command) || item.Command.Length > 8192 || item.Command.Contains('\0')) { Error = Text.Get("SshAgentInvalidTool"); return Task.CompletedTask; }
        return RunTaskAsync(Text.Get("SshAgentAnalyzeResult"), new("command", item.Command, true));
    }

    private async Task RunTaskAsync(string prompt, SshAgentAction? initialAction, Action? accepted = null)
    {
        if (IsBusy || _disposed) return;
        if (_connection() is not { } connection) { Error = Text.Get("SshWorkspaceUnavailable"); return; }
        long revision = _revision; string host = _host();
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; LastFailedPrompt = ""; Status = Text.Get("ChatWaitingModel");
        try
        {
            using var modelTask = ModelSession?.BeginTask(pending.Token, requireModel: initialAction is null);
            var taskToken = modelTask?.Token ?? pending.Token;
            string selected = IncludeOutput ? SelectedOutput : "";
            SshAgentToolResult? completed = null;
            if (initialAction is null) { Add(new(prompt, true, revision)); accepted?.Invoke(); }
            else
            {
                completed = await PerformActionAsync(initialAction, connection, host, revision, taskToken);
                EnsureCurrent(revision, connection, taskToken);
                if (completed.Status == "denied" || _conversation is null) return;
                selected = "";
            }
            if (_conversation is not null)
                await _conversation.RunAsync(prompt, host, selected,
                    (action, token) => PerformActionAsync(action, connection, host, revision, token),
                    reply => { EnsureCurrent(revision, connection, taskToken); Add(new(reply.Content, false, revision, reply.ToolCalls.Count == 0, modelIdentity: reply.ModelIdentity)); }, taskToken, initialAction, completed);
        }
        catch (OperationCanceledException) { if (!IsSourceClosed && revision == _revision) AddFailure(initialAction is null ? prompt : "", Text.Get("SshAgentCanceled")); }
        catch (Exception error) { if (!IsSourceClosed && revision == _revision) AddFailure(initialAction is null ? prompt : "", Text.Error(error, "AgentRequestFailed")); }
        finally { _pending = null; IsBusy = false; Status = ""; }
    }

    private async Task<SshAgentToolResult> PerformActionAsync(SshAgentAction action, ISshWorkspaceConnection connection, string host, long revision, CancellationToken token)
    {
        EnsureCurrent(revision, connection, token);
        var approval = action.RequiresConfirmation && _useInlineApproval ? new AgentApprovalRequest(Text.Get("ChatReviewOperation"), Text.Format("ChatSshApprovalTarget", host), action.ExecutionCommand, token) : null;
        var record = new SshAssistantMessage(action.Command, false, revision, false, action.RequiresConfirmation ? "SshAgentCommandStep" : "SshAgentDiagnosticStep", true, approval: approval);
        Add(record);
        bool dispatched = false;
        try
        {
            if (action.RequiresConfirmation)
            {
                Status = Text.Get("SshAgentAwaitingApproval"); record.Update(action.Command, "SshAgentAwaitingApproval");
                PendingApproval = approval;
                bool confirmed = approval is not null ? await approval.WaitAsync()
                    : await _dialogs.ConfirmActionAsync(Text.Get("SshAgentExecuteTitle"), Text.Format("SshAgentExecuteHint", host), action.ExecutionCommand).WaitAsync(token);
                EnsureCurrent(revision, connection, token);
                if (!confirmed) { record.Update(action.Command, "SshAgentActionDenied"); return new("denied", "", StopActions: true); }
            }
            Status = Text.Get(action.RequiresConfirmation ? "SshAgentExecuting" : "SshAgentDiagnosing");
            record.Update(action.Command, "SshAgentExecuting");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            dispatched = true;
            var result = await connection.ExecuteCommandAsync(action.ExecutionCommand, timeout.Token).WaitAsync(timeout.Token);
            EnsureCurrent(revision, connection, token);
            // 协议层在输出超限时可能提前终止读取，因此不能把该状态当作完整退出结果。
            bool unknown = result.Truncated && result.ExitCode == -1;
            record.Update(Text.Format("SshAgentStepResult", action.Command, result.ExitCode, result.Output) + (result.Truncated ? Environment.NewLine + Text.Get("SshOutputTruncated") : ""), unknown ? "SshAgentOutcomeUnknown" : "SshAgentStepCompleted");
            return new(unknown ? "unknown" : "completed", result.Output, unknown ? null : result.ExitCode, result.Truncated, unknown || action.RequiresConfirmation && result.ExitCode != 0);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            record.Update(action.Command, dispatched ? "SshAgentOutcomeUnknown" : "SshAgentNotExecuted"); throw;
        }
        catch (Exception error)
        {
            record.Update(action.Command, dispatched ? "SshAgentOutcomeUnknown" : "SshAgentNotExecuted");
            EnsureCurrent(revision, connection, token);
            string safe = Text.Error(error, "SshAgentCommandFailed");
            record.Update(Text.Format("SshAgentStepFailure", action.Command, safe), "SshAgentOutcomeUnknown");
            return new("unknown", safe, StopActions: true);
        }
        finally { PendingApproval = null; if (!IsSourceClosed && revision == _revision) Status = Text.Get("ChatWaitingModel"); }
    }

    private void Add(SshAssistantMessage message) { Messages.Add(message); while (Messages.Count > 48) Messages.RemoveAt(0); }
    private void AddFailure(string prompt, string error) { Error = error; LastFailedPrompt = prompt; Add(new(error, false, _revision, false, isError: true, recoveryPrompt: prompt)); }
    private void Refresh()
    {
        SendCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); ClearCommand?.NotifyCanExecuteChanged(); InsertCommand?.NotifyCanExecuteChanged(); ExecuteCommand?.NotifyCanExecuteChanged();
        RestoreDraftCommand?.NotifyCanExecuteChanged(); Archive?.Refresh();
    }
    /// <summary>连接代次改变即取消旧操作并禁用旧建议，保留可见消息和草稿。</summary>
    public void ConnectionChanged() { _revision++; _pending?.Cancel(); _conversation?.Clear(); SelectedOutput = ""; OnPropertyChanged(nameof(ContextLabel)); Refresh(); }
    /// <inheritdoc />
    public void Dispose() { CloseSource(); _disposed = true; InsertRequested = null; Refresh(); }
    /// <summary>来源结束仅撤销操作；窗口中已经显示的记录与草稿继续保留。</summary>
    public void CloseSource()
    {
        if (_sourceClosed) return; Archive.CloseSource(); ModelSession?.Dispose(); _sourceClosed = true; _pending?.Cancel(); _revision++; _conversation?.Clear(); OnPropertyChanged(nameof(IsSourceClosed)); Refresh();
    }
}
