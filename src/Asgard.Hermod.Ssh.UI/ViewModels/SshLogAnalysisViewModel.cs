using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>只分析明确附加的冻结日志，不自动发送，不具备远端执行权限。</summary>
public sealed class SshLogAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly LogAnalysisConversation? _conversation;
    private readonly SshContainerLogViewModel _document;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    /// <summary>与终端共享模型配置，但拥有独立历史与证据。</summary>
    public SshLogAnalysisViewModel(IAgentClient? client, SshContainerLogViewModel document)
    {
        _conversation = client is null ? null : new(client); _document = document;
        Archive = new(Messages, () => _conversation?.Capture() ?? AgentConversationSnapshot.Empty, state => _conversation?.Restore(state),
            () => Prompt, value => Prompt = value, () => IsBusy, () => new Asgard.Hermod.UI.Models.AgentConversationFeedback(Error, LastFailedPrompt, Attachment), value =>
            {
                var saved = value as Asgard.Hermod.UI.Models.AgentConversationFeedback ?? new();
                Attachment = saved.Attachment as SshLogAttachment; Error = saved.Error; LastFailedPrompt = saved.FailedPrompt;
            });
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !_disposed && !IsBusy && Prompt.Trim().Length > 0 && Attachment?.Text.Length > 0);
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        ClearCommand = new RelayCommand(_ => { _conversation?.Clear(); Messages.Clear(); Error = ""; }, _ => !IsBusy && !_disposed);
        AnalyzeCommand = new AsyncRelayCommand(_ => { if (PrepareAttachment(UseSelection) && Prompt.Length == 0) Prompt = Text.Get("SshLogAnalyzePrompt"); return Task.CompletedTask; },
            _ => !_disposed && _document.Snapshot is not null && (!UseSelection || Selection.Length > 0));
        RestoreDraftCommand = new RelayCommand(value => Prompt = value as string ?? LastFailedPrompt,
            value => !_disposed && !IsBusy && Prompt.Length == 0 && !string.IsNullOrEmpty(value as string ?? LastFailedPrompt));
    }
    /// <summary>日志文档的独立模型选择。</summary>
    public AgentModelSession? ModelSession => _conversation?.ModelSession;
    /// <summary>快捷提问只准备草稿。</summary>
    public IReadOnlyList<string> QuickPrompts { get; } = [Text.Get("ChatLogPromptOne"), Text.Get("ChatLogPromptTwo"), Text.Get("ChatLogPromptThree")];
    /// <summary>有界消息，关闭来源仍保留。</summary>
    public ObservableCollection<SshLogAnalysisMessage> Messages { get; } = [];
    /// <summary>仅清空已经接受的问题，允许编辑下一条。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>当前真实选区。</summary>
    public string Selection { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>选区在显示正文中的起点。</summary>
    public int SelectionStart { get; set; }
    /// <summary>验证 WPF 换行规范化后的真实选区。</summary>
    public string? DisplayedText { get; set; }
    /// <summary>空选区不会回退为整个日志。</summary>
    public bool UseSelection { get; set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>刷新不会改变已经明确附加的证据。</summary>
    public SshLogAttachment? Attachment { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>实际范围、采集时间与截断状态。</summary>
    public string ContextLabel => Attachment?.Summary ?? Text.Get("ChatLogNoAttachment");
    /// <summary>同一来源的历史。</summary>
    public AgentConversationArchive Archive { get; }
    /// <summary>关闭文档禁止请求；断线仍能分析已有快照。</summary>
    public bool IsSourceClosed => _disposed;
    /// <summary>当前请求状态。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>真实处理阶段。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>安全错误。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>失败问题可显式恢复，不覆盖草稿。</summary>
    public string LastFailedPrompt { get; private set; } = "";
    /// <summary>发送问题。</summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>准备附件与草稿，不自动发送。</summary>
    public AsyncRelayCommand AnalyzeCommand { get; }
    /// <summary>取消请求。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>清空当前对话。</summary>
    public RelayCommand ClearCommand { get; }
    /// <summary>恢复失败问题。</summary>
    public RelayCommand RestoreDraftCommand { get; }
    /// <summary>从成功快照冻结证据；选区已变化时拒绝分享。</summary>
    public bool PrepareAttachment(bool selection)
    {
        if (_disposed || _document.Snapshot is not { } snapshot) return false;
        try
        {
            if (selection && Selection.Length == 0) throw Text.Exception("ChatLogSelectionChanged");
            Attachment = SshLogAttachment.Freeze(snapshot, selection ? Selection : null, selection ? SelectionStart : 0, selection ? DisplayedText : null);
            Error = ""; return true;
        }
        catch (Exception error) { Error = Text.Error(error, "ChatLogSelectionChanged"); return false; }
    }
    private async Task SendAsync()
    {
        if (_disposed || IsBusy || Attachment is not { Text.Length: > 0 } attachment || string.IsNullOrWhiteSpace(Prompt)) return;
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt;
        using var responseStream = new AgentResponseStream<SshLogAnalysisMessage>(() => new("", false, attachment.Summary, attachment: attachment) { Question = prompt }, Add);
        using var pending = new CancellationTokenSource(); _pending = pending;
        IsBusy = true; Error = ""; LastFailedPrompt = ""; Status = Text.Get("ChatWaitingModel");
        Add(new(prompt, true, attachment.Summary, attachment: attachment)); Prompt = "";
        try
        {
            var reply = await _conversation.SendAsync(prompt, attachment.Source, attachment.Text, attachment.CapturedAt, attachment.IsTruncated, pending.Token,
                new(attachment.DocumentId, attachment.Host, attachment.ContainerId, attachment.RequestedLines, attachment.StartCharacter, attachment.EndCharacter, attachment.FromLine, attachment.ToLine, attachment.IsSelection), responseStream.ReceiveAsync);
            if (!_disposed) responseStream.Complete(new(reply.Content, false, attachment.Summary, modelIdentity: reply.ModelIdentity, attachment: attachment) { Question = prompt }, reply);
        }
        catch (OperationCanceledException) { if (!_disposed) Failed(Text.Get("SshAgentCanceled")); }
        catch (Exception error) { if (!_disposed) Failed(Text.Error(error, "AgentRequestFailed")); }
        finally { responseStream.Dispose(); _pending = null; Status = ""; IsBusy = false; }
        void Failed(string error)
        {
            Error = error; LastFailedPrompt = prompt;
            Add(new(error, false, attachment.Summary, isError: true, recoveryPrompt: prompt, attachment: attachment));
        }
    }
    private void Add(SshLogAnalysisMessage message) { Messages.Add(message); while (Messages.Count > 24) Messages.RemoveAt(0); }
    /// <summary>刷新命令，不更换冻结附件。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ContextLabel)); SendCommand?.NotifyCanExecuteChanged(); AnalyzeCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged(); ClearCommand?.NotifyCanExecuteChanged(); RestoreDraftCommand?.NotifyCanExecuteChanged(); Archive?.Refresh();
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Archive.CloseSource(); ModelSession?.Dispose(); _disposed = true; _pending?.Cancel(); _conversation?.Clear(); OnPropertyChanged(nameof(IsSourceClosed)); Refresh();
    }
}
