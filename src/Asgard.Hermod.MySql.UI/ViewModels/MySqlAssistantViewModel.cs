using Asgard.Hermod.Agent;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>每个页签的内存对话与 SQL 建议；查询仍由原页签的会话和草稿保护负责。</summary>
public sealed partial class MySqlAssistantViewModel : ObservableObject, IDisposable
{
    private readonly MySqlTabViewModel _tab;
    private readonly AgentConversation? _conversation;
    private CancellationTokenSource? _pending;
    private bool _disposed;
    private readonly bool _useInlineApproval;

    /// <summary>绑定原有页签；手动执行复用原会话，自动查询由页签创建独立只读会话。</summary>
    public MySqlAssistantViewModel(MySqlTabViewModel tab, IAgentClient? client, Action? hide = null, bool useInlineApproval = false)
    {
        _useInlineApproval = useInlineApproval;
        HideCommand = new RelayCommand(_ => hide?.Invoke());
        _tab = tab; _conversation = client is null ? null : new(client);
        Archive = new(Messages, () => _conversation?.Capture() ?? AgentConversationSnapshot.Empty, snapshot => _conversation?.Restore(snapshot), () => Prompt, value => Prompt = value, () => IsBusy,
            () => new Asgard.Hermod.UI.Models.AgentConversationFeedback(Error, LastFailedPrompt), state =>
            {
                var saved = state as Asgard.Hermod.UI.Models.AgentConversationFeedback ?? new();
                Error = saved.Error; LastFailedPrompt = saved.FailedPrompt;
            });
        RestoreDraftCommand = new RelayCommand(value => Prompt = value as string ?? LastFailedPrompt, value => !_disposed && !IsBusy && Prompt.Length == 0 && (value is string { Length: > 0 } || LastFailedPrompt.Length > 0));
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && !_disposed && !_tab.IsBusy && !_tab.IsManagingScope && _tab.ConnectionAvailable && !string.IsNullOrWhiteSpace(Prompt));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        InsertCommand = new RelayCommand(sql => { if (sql is MySqlAssistantSql suggestion) _tab.InsertAssistantSql(suggestion.Sql); }, _ => !_disposed && _tab.IsQuery && !_tab.IsBusy);
        ExecuteCommand = new AsyncRelayCommand(sql => sql is MySqlAssistantSql suggestion ? ExecuteSuggestionAsync(suggestion) : Task.CompletedTask, _ => !_disposed && _tab.CanRun && !IsBusy);
        ClearCommand = new AsyncRelayCommand(async _ => { if (await _tab.ConfirmRemovalAsync("DatabaseSessionClearAi")) { _conversation?.Clear(); Messages.Clear(); Error = ""; } }, _ => !IsBusy && !_disposed);
        _tab.PropertyChanged += OnTabChanged;
    }

    /// <summary>绑定当前数据库 Session 的模型选择，不跟随活动页签。</summary>
    public AgentModelSession? ModelSession => _conversation?.ModelSession;
    /// <summary>只收起分屏，不清除对话或输入，也不取消正在进行的请求。</summary>
    public RelayCommand HideCommand { get; }

    /// <summary>内存中的当前页签对话，关闭后清除。</summary>
    public ObservableCollection<MySqlAssistantMessage> Messages { get; } = [];
    /// <summary>快捷提问只填入草稿，仍需用户发送。</summary>
    public IReadOnlyList<string> QuickPrompts { get; } = [Text.Get("ChatDbPromptOne"), Text.Get("ChatDbPromptTwo"), Text.Get("ChatDbPromptThree")];
    /// <summary>同一 Session 的独立对话历史，来源关闭后仍可阅读。</summary>
    public AgentConversationArchive Archive { get; }
    /// <summary>来源关闭只撤销操作，不清除已显示记录。</summary>
    public bool IsSourceClosed => _disposed;
    /// <summary>当前请求的真实阶段，不模拟字符流。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>失败的问题可恢复到空草稿，不能覆盖用户正在写的下一条。</summary>
    public string LastFailedPrompt { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>只恢复文字，不重复执行请求或 SQL。</summary>
    public RelayCommand RestoreDraftCommand { get; }
    /// <summary>上下文显示真实数据库名。</summary>
    public string ContextLabel => Text.Format("DatabaseSessionAiContext", _tab.Name, string.Join(", ", _tab.Scope?.Databases.Select(item => item.Name) ?? [_tab.DatabaseContext]));
    /// <summary>范围变化后不发送旧范围结果。</summary>
    public bool CanShareResults => _tab.CanShareResults;
    /// <summary>用户发送的问题。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) { SendCommand?.NotifyCanExecuteChanged(); RestoreDraftCommand?.NotifyCanExecuteChanged(); } } } = "";
    /// <summary>用户可排除编辑器原文。</summary>
    public bool IncludeSql { get; set => SetProperty(ref field, value); } = true;
    /// <summary>默认不发送业务数据，用户明确选择后仅发送有限样本。</summary>
    public bool IncludeResults { get; set => SetProperty(ref field, value); }
    /// <summary>请求过程中保留完整草稿并允许取消。</summary>
    public bool IsBusy { get; private set => SetProperty(ref field, value); }
    /// <summary>安全错误不包含模型服务响应或 API Key。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>发送当前问题。</summary>
    public AsyncRelayCommand SendCommand { get; }
    /// <summary>取消当前元数据或模型请求。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>将建议应用到既有 SQL 输入框。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>确认后复用原会话执行建议，结果显示在原结果区。</summary>
    public AsyncRelayCommand ExecuteCommand { get; }
    /// <summary>清除本页签的内存对话。</summary>
    public AsyncRelayCommand ClearCommand { get; }

    private async Task SendAsync()
    {
        if (IsBusy || _disposed) return;
        if (IsAutomaticExecution) { await SendAutomaticAsync(); return; }
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; Refresh();
        LastFailedPrompt = ""; Status = Text.Get("ChatPreparingContext"); AddMessage(new(prompt, true)); Prompt = "";
        try
        {
            using var modelTask = ModelSession?.BeginTask(pending.Token);
            var taskToken = modelTask?.Token ?? pending.Token;
            long revision = _tab.ScopeRevision;
            var context = await _tab.ReadAssistantContextAsync(IncludeSql, IncludeResults, taskToken);
            if (revision != _tab.ScopeRevision) throw Text.Exception("MySqlAssistantContextChanged");
            var sessionIdentity = _tab.AssistantSessionIdentity;
            Status = Text.Get("ChatWaitingModel");
            var reply = await _conversation.SendAsync(prompt, context, taskToken);
            if (_disposed) return;
            if (revision != _tab.ScopeRevision) throw Text.Exception("MySqlAssistantContextChanged");
            AddMessage(new(reply.Content, false, reply.SqlBlocks.Select(sql => new MySqlAssistantSql(sql, context.Database, sessionIdentity, revision)).ToArray(), modelIdentity: reply.ModelIdentity) { Question = prompt });
        }
        catch (OperationCanceledException) { if (!_disposed) AddFailure(prompt, Text.Get("MySqlAssistantCancelled")); }
        catch (Exception error) { if (!_disposed) AddFailure(prompt, Text.Error(error, "AgentRequestFailed")); }
        finally { _pending = null; IsBusy = false; Status = ""; if (_disposed) _conversation.Clear(); Refresh(); }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (PendingApproval?.IsPending == true && args.PropertyName is nameof(MySqlTabViewModel.Database) or nameof(MySqlTabViewModel.ScopeRevision) or nameof(MySqlTabViewModel.ConnectionAvailable)) _pending?.Cancel();
        if (args.PropertyName is nameof(MySqlTabViewModel.Database) or nameof(MySqlTabViewModel.IsBusy) or nameof(MySqlTabViewModel.CanRun) or nameof(MySqlTabViewModel.ConnectionAvailable) or nameof(MySqlTabViewModel.ScopeRevision) or nameof(MySqlTabViewModel.CanShareResults)) Refresh();
    }
    private void Refresh()
    {
        OnPropertyChanged(nameof(CanChangeExecutionMode));
        OnPropertyChanged(nameof(ContextLabel)); OnPropertyChanged(nameof(CanShareResults)); SendCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        InsertCommand.NotifyCanExecuteChanged(); ExecuteCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged();
        RestoreDraftCommand.NotifyCanExecuteChanged(); Archive.Refresh();
    }
    private void AddMessage(MySqlAssistantMessage message) { Messages.Add(message); while (Messages.Count > 48) Messages.RemoveAt(0); }
    private void AddFailure(string prompt, string error) { Error = error; LastFailedPrompt = prompt; AddMessage(new(error, false, isError: true, recoveryPrompt: prompt)); }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        Archive.CloseSource(); ModelSession?.Dispose(); _disposed = true; _pending?.Cancel(); _tab.PropertyChanged -= OnTabChanged; OnPropertyChanged(nameof(IsSourceClosed)); Refresh();
        if (!IsBusy) _conversation?.Clear();
    }
}
