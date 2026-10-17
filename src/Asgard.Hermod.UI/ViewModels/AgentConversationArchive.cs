using System.Collections;
using System.Collections.Specialized;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.UI.ViewModels;

/// <summary>来源内的对话历史；切换只恢复内存，不触发模型请求、SQL 或 SSH 操作。</summary>
public sealed class AgentConversationArchive : ObservableObject
{
    private readonly IList _messages;
    private readonly Func<AgentConversationSnapshot> _capture;
    private readonly Action<AgentConversationSnapshot> _restore;
    private readonly Func<string> _getDraft;
    private readonly Action<string> _setDraft;
    private readonly Func<bool> _isBusy;
    private readonly Func<object?>? _captureContext;
    private readonly Action<object?>? _restoreContext;
    private bool _switching, _sourceClosed;

    /// <summary>消息与业务引擎仍由模块拥有，归档不读取或持久化外部数据。</summary>
    public AgentConversationArchive(IList messages, Func<AgentConversationSnapshot> capture, Action<AgentConversationSnapshot> restore, Func<string> getDraft, Action<string> setDraft, Func<bool> isBusy, Func<object?>? captureContext = null, Action<object?>? restoreContext = null)
    {
        _messages = messages; _capture = capture; _restore = restore; _getDraft = getDraft; _setDraft = setDraft; _isBusy = isBusy;
        _captureContext = captureContext; _restoreContext = restoreContext;
        ActiveThread = new(); Threads.Add(ActiveThread);
        NewCommand = new RelayCommand(_ => StartNew(), _ => !_sourceClosed && !_isBusy());
        if (messages is INotifyCollectionChanged observed) observed.CollectionChanged += (_, _) => { if (!_switching) UpdateTitle(); };
    }
    /// <summary>每个来源保留最近 24 个内存对话。</summary>
    public ObservableCollection<AgentConversationThread> Threads { get; } = [];
    /// <summary>同一集合上的当前对话页，不改变来源身份。</summary>
    public AgentConversationThread ActiveThread
    {
        get;
        set
        {
            if (ReferenceEquals(field, value) || value is null) return;
            if (field is not null)
            {
                if (!CanSwitch || !Threads.Contains(value)) return;
                CaptureCurrent(field); RestoreThread(value);
            }
            field = value; OnPropertyChanged();
        }
    } = null!;
    /// <summary>忙碌任务先停止，来源关闭后仍允许阅读历史。</summary>
    public bool CanSwitch => !_isBusy();
    /// <summary>归档当前页并开始一个新对话，不删除已有历史。</summary>
    public RelayCommand NewCommand { get; }
    /// <summary>模块状态改变时刷新原生命令状态。</summary>
    public void Refresh() { OnPropertyChanged(nameof(CanSwitch)); NewCommand.NotifyCanExecuteChanged(); }
    /// <summary>关闭来源保留界面历史，后续历史选择不再改动已释放的引擎。</summary>
    public void CloseSource() { CaptureCurrent(ActiveThread); _sourceClosed = true; Refresh(); }

    private void StartNew()
    {
        var next = new AgentConversationThread(); Threads.Add(next); ActiveThread = next;
        while (Threads.Count > 24) Threads.RemoveAt(0);
    }
    private void CaptureCurrent(AgentConversationThread thread)
    {
        thread.Messages = _messages.Cast<object>().ToArray(); thread.Draft = _getDraft();
        thread.ContextState = _captureContext?.Invoke();
        if (!_sourceClosed) thread.EngineState = _capture();
    }
    private void RestoreThread(AgentConversationThread thread)
    {
        if (!_sourceClosed) _restore(thread.EngineState);
        _switching = true;
        try { _messages.Clear(); foreach (var message in thread.Messages) _messages.Add(message); _setDraft(thread.Draft); _restoreContext?.Invoke(thread.ContextState); }
        finally { _switching = false; }
    }
    private void UpdateTitle()
    {
        string? first = _messages.OfType<IAgentDisplayMessage>().FirstOrDefault(message => message.IsUser)?.Content.Trim();
        ActiveThread.Title = string.IsNullOrEmpty(first) ? Text.Get("AgentNewConversation") : first.Length <= 48 ? first : first[..48] + "…";
    }
}
