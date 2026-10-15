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
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private long _revision;

    /// <summary>复用模型和已认证的 SSH 连接；连接凭据从不进入模型上下文。</summary>
    public SshAssistantViewModel(IAgentClient? client, Func<ISshWorkspaceConnection?> connection, Func<string> host, ISshWorkspaceDialogs dialogs)
    {
        _conversation = client is null ? null : new(client); _connection = connection; _host = host; _dialogs = dialogs;
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !_disposed && !IsBusy && !string.IsNullOrWhiteSpace(Prompt));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        ClearCommand = new RelayCommand(_ => { _conversation?.Clear(); Messages.Clear(); Error = ""; }, _ => !IsBusy && !_disposed);
        InsertCommand = new RelayCommand(value => { if (value is SshAssistantCommand item && IsCurrent(item)) InsertRequested?.Invoke(this, item.Command); }, value => value is SshAssistantCommand item && IsCurrent(item) && !IsBusy);
        ExecuteCommand = new AsyncRelayCommand(value => value is SshAssistantCommand item ? ExecuteAsync(item) : Task.CompletedTask, value => value is SshAssistantCommand item && IsCurrent(item) && !IsBusy);
    }

    /// <summary>用户明确填入终端，不添加回车。</summary>
    public event EventHandler<string>? InsertRequested;
    /// <summary>有界显示历史，工具步骤也计入上限。</summary>
    public ObservableCollection<SshAssistantMessage> Messages { get; } = [];
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

    private bool IsCurrent(SshAssistantCommand item) => !_disposed && item.Revision == _revision && _connection() is not null;
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
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; Status = Text.Get("SshAgentThinking");
        try
        {
            string selected = IncludeOutput ? SelectedOutput : "";
            SshAgentToolResult? completed = null;
            if (initialAction is null) Add(new(prompt, true, revision));
            else
            {
                completed = await PerformActionAsync(initialAction, connection, host, revision, pending.Token);
                EnsureCurrent(revision, connection, pending.Token);
                if (completed.Status == "denied" || _conversation is null) return;
                selected = "";
            }
            if (_conversation is not null)
                await _conversation.RunAsync(prompt, host, selected,
                    (action, token) => PerformActionAsync(action, connection, host, revision, token),
                    reply => { EnsureCurrent(revision, connection, pending.Token); Add(new(reply.Content, false, revision, reply.ToolCalls.Count == 0)); }, pending.Token, initialAction, completed);
            accepted?.Invoke();
        }
        catch (OperationCanceledException) { if (!_disposed && revision == _revision) Error = Text.Get("SshAgentCanceled"); }
        catch (Exception error) { if (!_disposed && revision == _revision) Error = Text.Error(error, "AgentRequestFailed"); }
        finally { _pending = null; IsBusy = false; Status = ""; }
    }

    private async Task<SshAgentToolResult> PerformActionAsync(SshAgentAction action, ISshWorkspaceConnection connection, string host, long revision, CancellationToken token)
    {
        EnsureCurrent(revision, connection, token);
        var record = new SshAssistantMessage(action.Command, false, revision, false, action.RequiresConfirmation ? "SshAgentCommandStep" : "SshAgentDiagnosticStep", true);
        Add(record);
        bool dispatched = false;
        try
        {
            if (action.RequiresConfirmation)
            {
                Status = Text.Get("SshAgentAwaitingApproval"); record.Update(action.Command, "SshAgentAwaitingApproval");
                bool confirmed = await _dialogs.ConfirmActionAsync(Text.Get("SshAgentExecuteTitle"), Text.Format("SshAgentExecuteHint", host), action.ExecutionCommand);
                EnsureCurrent(revision, connection, token);
                if (!confirmed) { record.Update(action.Command, "SshAgentActionDenied"); return new("denied", "", StopActions: true); }
            }
            Status = Text.Get(action.RequiresConfirmation ? "SshAgentExecuting" : "SshAgentDiagnosing");
            record.Update(action.Command, "SshAgentExecuting");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
            dispatched = true;
            var result = await connection.ExecuteCommandAsync(action.ExecutionCommand, timeout.Token);
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
        finally { if (!_disposed && revision == _revision) Status = Text.Get("SshAgentThinking"); }
    }

    private void Add(SshAssistantMessage message) { Messages.Add(message); while (Messages.Count > 48) Messages.RemoveAt(0); }
    private void Refresh()
    {
        SendCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); ClearCommand?.NotifyCanExecuteChanged(); InsertCommand?.NotifyCanExecuteChanged(); ExecuteCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>连接代次改变即取消旧操作并禁用旧建议，保留可见消息和草稿。</summary>
    public void ConnectionChanged() { _revision++; _pending?.Cancel(); _conversation?.Clear(); SelectedOutput = ""; OnPropertyChanged(nameof(ContextLabel)); Refresh(); }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _pending?.Cancel(); InsertRequested = null; }
}
