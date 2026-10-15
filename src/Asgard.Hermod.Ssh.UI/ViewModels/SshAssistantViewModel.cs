using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>当前主机的内存助手；隐藏分屏不取消请求、不销毁草稿，也不自动展开窗口。</summary>
public sealed class SshAssistantViewModel : ObservableObject, IDisposable
{
    private readonly SshAgentConversation? _conversation;
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly Func<string> _host;
    private readonly ISshWorkspaceDialogs _dialogs;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private long _revision;

    /// <summary>复用用户在设置中配置的 Agent 客户端，协议凭据始终不进入请求。</summary>
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
    /// <summary>有界显示历史。</summary>
    public ObservableCollection<SshAssistantMessage> Messages { get; } = [];
    /// <summary>收起后保留的草稿。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>用户明确附加的终端选区，最多八千字符。</summary>
    public string SelectedOutput { get; set { if (SetProperty(ref field, value.Length <= 8192 ? value : value[..8192])) OnPropertyChanged(nameof(ContextLabel)); } } = "";
    /// <summary>发送选区需要明确选择。</summary>
    public bool IncludeOutput { get; set => SetProperty(ref field, value); }
    /// <summary>上下文内容与长度可见。</summary>
    public string ContextLabel => Text.Format("SshAgentContext", _host(), SelectedOutput.Length);
    /// <summary>请求与命令串行，不因回复抢占焦点。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>安全错误资源。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>发送问题。</summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>显式停止当前请求或命令。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>新对话。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>仅填入终端。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>确认后执行命令。</summary>
    public AsyncRelayCommand ExecuteCommand { get; }

    private bool IsCurrent(SshAssistantCommand item) => !_disposed && item.Revision == _revision && _connection() is not null;
    private async Task SendAsync()
    {
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt; long revision = _revision;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = "";
        try
        {
            var reply = await _conversation.SendAsync(prompt, _host(), IncludeOutput ? SelectedOutput : "", pending.Token);
            if (_disposed || revision != _revision) return;
            Messages.Add(new(prompt, true, revision)); Messages.Add(new(reply.Content, false, revision)); Trim();
            if (Prompt == prompt) Prompt = "";
        }
        catch (OperationCanceledException) { if (!_disposed) Error = Text.Get("SshAgentCanceled"); }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error, "AgentRequestFailed"); }
        finally { _pending = null; IsBusy = false; }
    }

    private async Task ExecuteAsync(SshAssistantCommand item)
    {
        if (!IsCurrent(item) || _connection() is not { } connection) return;
        if (!await _dialogs.ConfirmActionAsync(Text.Get("SshAgentExecuteTitle"), Text.Format("SshAgentExecuteHint", _host()), item.Command)) return;
        if (!IsCurrent(item) || !ReferenceEquals(connection, _connection())) return;
        using var pending = new CancellationTokenSource(TimeSpan.FromMinutes(2)); _pending = pending; IsBusy = true; Error = "";
        try
        {
            var result = await connection.ExecuteCommandAsync(item.Command, pending.Token);
            if (!IsCurrent(item)) return;
            Messages.Add(new(Text.Format("SshAgentCommandResult", result.ExitCode, result.Output) + (result.Truncated ? Environment.NewLine + Text.Get("SshOutputTruncated") : ""), false, _revision, false)); Trim();
        }
        catch (OperationCanceledException) { if (!_disposed) Error = Text.Get("SshAgentCanceled"); }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error, "SshAgentCommandFailed"); }
        finally { _pending = null; IsBusy = false; }
    }

    private void Trim() { while (Messages.Count > 24) Messages.RemoveAt(0); }
    private void Refresh()
    {
        SendCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); ClearCommand?.NotifyCanExecuteChanged(); InsertCommand?.NotifyCanExecuteChanged(); ExecuteCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>连接代次改变即取消旧操作并禁用旧建议，保留可见对话和草稿。</summary>
    public void ConnectionChanged() { _revision++; _pending?.Cancel(); _conversation?.Clear(); SelectedOutput = ""; OnPropertyChanged(nameof(ContextLabel)); Refresh(); }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _pending?.Cancel(); InsertRequested = null; }
}
