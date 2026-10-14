using Asgard.Hermod.MySql.UI.Services;

namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>表与查询页签拥有独立的物理会话、SQL 文稿和结果草稿。</summary>
public sealed partial class MySqlTabViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IMySqlSessionFactory _factory;
    private readonly InstanceConnectionOptions _options;
    private readonly IMySqlWorkspaceDialogs _dialogs;
    private readonly CancellationTokenSource _lifetime;
    private CancellationTokenSource? _pending;
    private IMySqlSession? _session;
    private TaskCompletionSource? _completion;
    private string _savedSql = "";
    private string? _path;
    private bool _disposed;
    private bool _executed;
    /// <summary>视图提交正在输入的原生表格单元格，验证失败时阻止保存、刷新和关闭。</summary>
    public Func<bool>? CommitInputRequested { get; set; }
    public MySqlTabViewModel(string name, string database, MySqlObjectKind kind, IMySqlSessionFactory factory,
        InstanceConnectionOptions options, IMySqlWorkspaceDialogs dialogs, CancellationToken lifetime = default)
    {
        Name = name; Database = database; Kind = kind; _factory = factory; _options = options; _dialogs = dialogs;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        RefreshCommand = new AsyncRelayCommand(_ => ReadAsync(), _ => !IsBusy && !IsQuery);
        ExecuteCommand = new AsyncRelayCommand(sql => ExecuteAsync(sql as string ?? Sql), _ => !IsBusy && IsQuery);
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
        SaveDocumentCommand = new AsyncRelayCommand(_ => SaveDocumentAsync(), _ => IsQuery && !IsBusy);
        SaveChangesCommand = new AsyncRelayCommand(_ => SaveChangesAsync(), _ => !IsBusy && HasDataDraft && !OutcomeUnknown);
        RevertCommand = new RelayCommand(_ => { foreach (var grid in Results) grid.Revert(); }, _ => !IsBusy && HasDataDraft && !OutcomeUnknown);
        VerifyCommand = new AsyncRelayCommand(_ => VerifyAsync(), _ => !IsBusy && OutcomeUnknown);
        PreviousCommand = new AsyncRelayCommand(_ => PageAsync(-200), _ => !IsBusy && !IsQuery && Offset > 0);
        NextCommand = new AsyncRelayCommand(_ => PageAsync(200), _ => !IsBusy && !IsQuery && SelectedResult?.Rows.Count == 200);
    }
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Name { get; }
    public MySqlObjectKind Kind { get; }
    public string Database { get; private set; }
    public bool IsQuery => Kind == MySqlObjectKind.Root;
    public bool IsMetadataOnly => Kind is MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Event or MySqlObjectKind.Trigger;
    public string Definition { get; private set; } = "";
    public string Header => IsQuery ? Name : Text.Format("MySqlObjectTitle", Database, Name);
    public string Icon => IsQuery ? "file-code" : Kind switch { MySqlObjectKind.View => "eye", MySqlObjectKind.Procedure => "workflow", MySqlObjectKind.Function => "square-function", MySqlObjectKind.Event => "clock-3", _ => "table-2" };
    public string Sql { get; set { if (SetProperty(ref field, value)) DraftChanged(); } } = "";
    public bool IsDocumentDirty => IsQuery && Sql != _savedSql;
    public bool HasDataDraft => Results.Any(grid => grid.IsDirty);
    public bool IsDirty => HasDataDraft || IsDocumentDirty;
    public bool IsActive { get; set => SetProperty(ref field, value); }
    public bool IsBusy { get; private set; }
    public bool OutcomeUnknown { get; private set; }
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public string Message { get; private set; } = "";
    public bool HasMessage => Message.Length > 0;
    public string Status { get; private set; } = Text.Get("MySqlReady");
    public string DraftSummary => Text.Format("MySqlPendingChanges", Results.Sum(grid => grid.DirtyCount));
    public string EditHint => Text.Get(SelectedResult?.IsEditable == true ? "MySqlInlineEditHint" : "MySqlReadOnlyResult");
    public string StateLabel => Text.Format("MySqlSessionStatus", Text.Get(_session?.State.Autocommit switch { true => "MySqlOn", false => "MySqlOff", _ => "MySqlUnknown" }),
        Text.Get(_session?.State.InTransaction switch { true => "MySqlTransactionActive", false => "MySqlTransactionNone", _ => "MySqlUnknown" }));
    public IReadOnlyList<string> Databases { get; internal set => SetProperty(ref field, value); } = [];
    public ObservableCollection<MySqlGridViewModel> Results { get; } = [];
    public MySqlGridViewModel? SelectedResult { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(EditHint)); OnPropertyChanged(nameof(HasResult)); } } }
    public bool HasResult => SelectedResult is not null;
    public IReadOnlyList<MySqlObject> Structure { get; private set; } = [];
    public IReadOnlyList<InstanceFieldChoice> Sections { get; } = [new("data", Text.Get("MySqlData")), new("structure", Text.Get("MySqlStructure"))];
    public string Section { get; set => SetProperty(ref field, value); } = "data";
    public int Offset { get; private set; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand ExecuteCommand { get; }
    public RelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveDocumentCommand { get; }
    public AsyncRelayCommand SaveChangesCommand { get; }
    public RelayCommand RevertCommand { get; }
    public AsyncRelayCommand VerifyCommand { get; }
    public AsyncRelayCommand PreviousCommand { get; }
    public AsyncRelayCommand NextCommand { get; }

    private async Task RunAsync(Func<CancellationToken, Task> operation)
    {
        if (IsBusy || _disposed) return;
        using var pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _pending = pending; _completion = new(TaskCreationOptions.RunContinuationsAsynchronously); IsBusy = true; Error = ""; Refresh();
        try { await operation(pending.Token); }
        catch (OperationCanceledException) when (pending.IsCancellationRequested) { Error = Text.Get("MySqlQueryCancelled"); }
        catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); }
        finally { IsBusy = false; _pending = null; _completion.TrySetResult(); Refresh(); }
    }
    private async Task<IMySqlSession> SessionAsync(CancellationToken token)
    {
        if (_session is null) _session = await _factory.OpenAsync(_options, token);
        if (!_session.IsConnected) throw new MySqlFailure("MySqlConnectionFailed", "MYSQL_CONNECTION");
        return _session;
    }
    public async Task ReadAsync()
    {
        if (IsQuery || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        await RunAsync(async token =>
        {
            var session = await SessionAsync(token);
            if (!IsMetadataOnly)
            {
                var result = await session.ReadTableAsync(Database, Name, Offset, token); SetResults([result]);
                Status = Text.Format("MySqlPageStatus", result.Rows.Count == 0 ? 0 : Offset + 1, Offset + result.Rows.Count);
            }
            var structure = new List<MySqlObject>();
            foreach (var kind in IsMetadataOnly ? new[] { MySqlObjectKind.Parameters } : Kind == MySqlObjectKind.View ? [MySqlObjectKind.Columns] : new[] { MySqlObjectKind.Columns, MySqlObjectKind.Indexes, MySqlObjectKind.ForeignKeys, MySqlObjectKind.Triggers })
            {
                try { var page = await session.ListAsync(new(kind, "", Database, Name), 0, token); structure.AddRange(page.Items); }
                catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); }
            }
            Structure = structure; OnPropertyChanged(nameof(Structure));
            try { Definition = await session.ReadDefinitionAsync(new(Kind, Name, Database), token); OnPropertyChanged(nameof(Definition)); }
            catch (Exception error) { Error = Text.Error(error, "MySqlCommandFailed"); }
        });
    }
    private async Task PageAsync(int delta) { if (!await ResolveDataDraftAsync()) return; Offset = Math.Max(0, Offset + delta); OnPropertyChanged(nameof(Offset)); await ReadAsync(); }
    public async Task ExecuteAsync(string sql)
    {
        if (!IsQuery || sql.Length == 0 || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        await RunAsync(async token =>
        {
            var session = await SessionAsync(token); _executed = true;
            var execution = await session.ExecuteAsync(sql, Database, token);
            Database = session.CurrentDatabase;
            SetResults(execution.Results);
            Error = execution.Failure is null ? "" : Text.Error(execution.Failure, "MySqlCommandFailed");
            Message = string.Join(Environment.NewLine, execution.Outcomes.Select(outcome => Text.Format(outcome.Succeeded ? "MySqlStatementSucceeded" : "MySqlStatementStopped", outcome.Statement, Math.Max(0, outcome.AffectedRows))));
            Status = Text.Format("MySqlExecutionStatus", execution.Outcomes.Count, execution.Elapsed.TotalMilliseconds);
        });
    }
    private void SetResults(IEnumerable<MySqlResult> results)
    {
        foreach (var grid in Results) grid.DraftChanged -= DraftChanged; Results.Clear();
        foreach (var result in results) { var grid = new MySqlGridViewModel(result); grid.DraftChanged += DraftChanged; Results.Add(grid); }
        SelectedResult = Results.FirstOrDefault(); DraftChanged();
    }
    private void DraftChanged()
    {
        foreach (string property in new[] { nameof(IsDirty), nameof(IsDocumentDirty), nameof(HasDataDraft), nameof(DraftSummary) }) OnPropertyChanged(property);
        SaveChangesCommand?.NotifyCanExecuteChanged(); RevertCommand?.NotifyCanExecuteChanged();
    }
    private void Refresh()
    {
        foreach (string property in new[] { nameof(IsBusy), nameof(Error), nameof(HasError), nameof(Message), nameof(HasMessage), nameof(Status), nameof(StateLabel), nameof(OutcomeUnknown), nameof(EditHint), nameof(Header), nameof(Database) }) OnPropertyChanged(property);
        foreach (var command in new[] { RefreshCommand, ExecuteCommand, SaveDocumentCommand, SaveChangesCommand, VerifyCommand, PreviousCommand, NextCommand }) command.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged(); RevertCommand.NotifyCanExecuteChanged(); DraftChanged();
    }
}
