using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>日志专用对话，分析全部快照或用户选区，不自动发送，不具备远端执行权限。</summary>
public sealed class SshLogAnalysisViewModel : ObservableObject, IDisposable
{
    private readonly LogAnalysisConversation? _conversation;
    private readonly SshContainerLogViewModel _document;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <summary>与终端助手使用相同模型配置，但拥有独立对话和上下文。</summary>
    public SshLogAnalysisViewModel(IAgentClient? client, SshContainerLogViewModel document)
    {
        _conversation = client is null ? null : new(client); _document = document;
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !_disposed && !IsBusy && Prompt.Trim().Length > 0 && ContextText.Length > 0);
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        ClearCommand = new RelayCommand(_ => { _conversation?.Clear(); Messages.Clear(); Error = ""; }, _ => !IsBusy && !_disposed);
        AnalyzeCommand = new AsyncRelayCommand(_ => { Prompt = Text.Get("SshLogAnalyzePrompt"); return SendAsync(); }, _ => !_disposed && !IsBusy && ContextText.Length > 0);
    }

    /// <summary>窗口内独立的有界对话。</summary>
    public ObservableCollection<SshLogAnalysisMessage> Messages { get; } = [];
    /// <summary>发送期间用户新写的草稿不会被清空。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>由日志阅读器的真实文本选区更新。</summary>
    public string Selection { get; set { if (SetProperty(ref field, value)) Refresh(); } } = "";
    /// <summary>用户可选择只发送选区；空选区不会悄悄改为整个日志。</summary>
    public bool UseSelection { get; set { if (SetProperty(ref field, value)) Refresh(); } }
    private string ContextText => UseSelection ? Selection : _document.LogText;
    /// <summary>实际发送字符数与截断状态可见。</summary>
    public string ContextLabel => Text.Format(UseSelection ? "SshLogSelectedContext" : "SshLogSnapshotContext",
        Math.Min(ContextText.Length, LogAnalysisConversation.ContextLimit)) +
        (ContextText.Length > LogAnalysisConversation.ContextLimit || !UseSelection && _document.IsTruncated ? " " + Text.Get("SshLogContextLimited") : "");
    /// <summary>进行中的请求可取消。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>模型错误经过共享安全资源翻译。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>发送显式问题。</summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>一键分析仍由用户显式触发。</summary>
    public AsyncRelayCommand AnalyzeCommand { get; }
    /// <summary>停止请求。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>清空当前日志窗口的对话。</summary>
    public RelayCommand ClearCommand { get; }

    private async Task SendAsync()
    {
        if (_disposed || IsBusy || ContextText.Length == 0) return;
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt, context = ContextText, label = ContextLabel;
        bool incomplete = context.Length > LogAnalysisConversation.ContextLimit || _document.IsTruncated;
        if (context.Length > LogAnalysisConversation.ContextLimit) context = context[^LogAnalysisConversation.ContextLimit..];
        using var pending = new CancellationTokenSource(TimeSpan.FromMinutes(2)); _pending = pending;
        IsBusy = true; Error = ""; Add(new(prompt, true, label));
        try
        {
            var reply = await _conversation.SendAsync(prompt, _document.Source, context, _document.CapturedAt, incomplete, pending.Token);
            if (_disposed) return;
            Add(new(reply.Content, false, label)); if (Prompt == prompt) Prompt = "";
        }
        catch (OperationCanceledException) { if (!_disposed) Error = Text.Get("SshAgentCanceled"); }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error, "AgentRequestFailed"); }
        finally { _pending = null; IsBusy = false; }
    }

    private void Add(SshLogAnalysisMessage message) { Messages.Add(message); while (Messages.Count > 24) Messages.RemoveAt(0); }
    /// <summary>新快照改变将要发送的范围，不改写已经显示的对话。</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ContextLabel)); SendCommand?.NotifyCanExecuteChanged(); AnalyzeCommand?.NotifyCanExecuteChanged();
        CancelCommand?.NotifyCanExecuteChanged(); ClearCommand?.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose() { _disposed = true; _pending?.Cancel(); _conversation?.Clear(); }
}
