using Asgard.Hermod.Agent;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>每个页签的内存对话与 SQL 建议；查询仍由原页签的会话和草稿保护负责。</summary>
public sealed class PostgreSqlAssistantViewModel : ObservableObject, IDisposable
{
    private readonly PostgreSqlTabViewModel _tab;
    private readonly AgentConversation? _conversation;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <summary>绑定原有页签，不创建第二个数据库连接。</summary>
    public PostgreSqlAssistantViewModel(PostgreSqlTabViewModel tab, IAgentClient? client)
    {
        _tab = tab; _conversation = client is null ? null : new(client);
        SendCommand = new AsyncRelayCommand(_ => SendAsync(), _ => !IsBusy && !_disposed && !_tab.IsBusy && _tab.ConnectionAvailable && !string.IsNullOrWhiteSpace(Prompt));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        InsertCommand = new RelayCommand(sql => { if (sql is PostgreSqlAssistantSql suggestion) _tab.InsertAssistantSql(suggestion.Sql); }, _ => !_disposed && _tab.IsQuery && !_tab.IsBusy);
        ExecuteCommand = new AsyncRelayCommand(sql => sql is PostgreSqlAssistantSql suggestion ? _tab.ExecuteAssistantSqlAsync(suggestion) : Task.CompletedTask, _ => !_disposed && _tab.CanRun && !IsBusy);
        ClearCommand = new RelayCommand(_ => { _conversation?.Clear(); Messages.Clear(); Error = ""; }, _ => !IsBusy);
        _tab.PropertyChanged += OnTabChanged;
    }

    /// <summary>内存中的当前页签对话，关闭后清除。</summary>
    public ObservableCollection<PostgreSqlAssistantMessage> Messages { get; } = [];
    /// <summary>上下文显示真实数据库名。</summary>
    public string ContextLabel => Text.Format("PostgreSqlAssistantContext", _tab.DatabaseContext, _tab.Name);
    /// <summary>用户发送的问题。</summary>
    public string Prompt { get; set { if (SetProperty(ref field, value)) SendCommand?.NotifyCanExecuteChanged(); } } = "";
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
    public RelayCommand ClearCommand { get; }

    private async Task SendAsync()
    {
        if (IsBusy || _disposed) return;
        if (_conversation is null) { Error = Text.Get("AgentConfigurationRequired"); return; }
        string prompt = Prompt;
        using var pending = new CancellationTokenSource(); _pending = pending; IsBusy = true; Error = ""; Refresh();
        try
        {
            var context = await _tab.ReadAssistantContextAsync(IncludeSql, IncludeResults, pending.Token);
            var sessionIdentity = _tab.AssistantSessionIdentity;
            var reply = await _conversation.SendAsync(prompt, context, pending.Token);
            if (_disposed) return;
            Messages.Add(new(prompt, true)); Messages.Add(new(reply.Content, false,
                reply.SqlBlocks.Select(sql => new PostgreSqlAssistantSql(sql, context.Database, sessionIdentity)).ToArray()));
            while (Messages.Count > 24) Messages.RemoveAt(0);
            if (Prompt == prompt) Prompt = "";
        }
        catch (OperationCanceledException) { if (!_disposed) Error = Text.Get("PostgreSqlAssistantCancelled"); }
        catch (Exception error) { if (!_disposed) Error = Text.Error(error, "AgentRequestFailed"); }
        finally { _pending = null; IsBusy = false; if (_disposed) _conversation.Clear(); Refresh(); }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(PostgreSqlTabViewModel.Database) or nameof(PostgreSqlTabViewModel.IsBusy) or nameof(PostgreSqlTabViewModel.CanRun) or nameof(PostgreSqlTabViewModel.ConnectionAvailable)) Refresh();
    }
    private void Refresh()
    {
        OnPropertyChanged(nameof(ContextLabel)); SendCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        InsertCommand.NotifyCanExecuteChanged(); ExecuteCommand.NotifyCanExecuteChanged(); ClearCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _pending?.Cancel(); _tab.PropertyChanged -= OnTabChanged; Messages.Clear(); Prompt = "";
        if (!IsBusy) _conversation?.Clear();
    }
}
