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
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
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
    /// <summary>此页签在当前工作区中的稳定身份。</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");
    /// <summary>对象原始名称或已本地化的文稿标题。</summary>
    public string Name { get; }
    /// <summary>对象类型标识，不以显示标题控制行为。</summary>
    public MySqlObjectKind Kind { get; }
    /// <summary>此页签或连接的数据库上下文，不由树选择隐式修改。</summary>
    public string Database { get; private set; }
    /// <summary>是否为独立 SQL 文稿页签。</summary>
    public bool IsQuery => Kind == MySqlObjectKind.Root;
    /// <summary>是否只展示定义和参数。</summary>
    public bool IsMetadataOnly => Kind is MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Event or MySqlObjectKind.Trigger;
    /// <summary>服务器返回的 DDL 原文，只读展示，不自动执行。</summary>
    public string Definition { get; private set; } = "";
    /// <summary>完整页签标题，截断只由公共视图负责。</summary>
    public string Header => IsQuery ? Name : Text.Format("MySqlObjectTitle", Database, Name);
    /// <summary>按真实对象类型选择的公共图标。</summary>
    public string Icon => IsQuery ? "file-code" : Kind switch { MySqlObjectKind.View => "eye", MySqlObjectKind.Procedure => "workflow", MySqlObjectKind.Function => "square-function", MySqlObjectKind.Event => "clock-3", _ => "table-2" };
    /// <summary>当前 SQL 文稿，与表格草稿独立。</summary>
    public string Sql { get; set { if (SetProperty(ref field, value)) DraftChanged(); } } = "";
    /// <summary>SQL 文件内容尚未保存。</summary>
    public bool IsDocumentDirty => IsQuery && Sql != _savedSql;
    /// <summary>结果中存在待提交字段。</summary>
    public bool HasDataDraft => Results.Any(grid => grid.IsDirty);
    /// <summary>存在未应用或未提交的修改。</summary>
    public bool IsDirty => HasDataDraft || IsDocumentDirty;
    /// <summary>当前内部页签的选择状态。</summary>
    public bool IsActive { get; set => SetProperty(ref field, value); }
    /// <summary>连接或命令正在执行，禁止重复操作。</summary>
    public bool IsBusy { get; private set; }
    /// <summary>当前允许启动新的 SQL 执行。</summary>
    public bool CanRun => IsQuery && !IsBusy;
    /// <summary>提交结果未确认，阻止盲目重试。</summary>
    public bool OutcomeUnknown { get; private set; }
    /// <summary>可安全展示的本地化错误。</summary>
    public string Error { get; private set; } = "";
    /// <summary>当前是否有需要呈现的错误。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>已完成语句或保存结果的安全说明。</summary>
    public string Message { get; private set; } = "";
    /// <summary>是否存在执行说明。</summary>
    public bool HasMessage => Message.Length > 0;
    /// <summary>读取状态或安全失败说明。</summary>
    public string Status { get; private set; } = Text.Get("MySqlReady");
    /// <summary>待保存字段数量的本地化说明。</summary>
    public string DraftSummary => Text.Format("MySqlPendingChanges", Results.Sum(grid => grid.DirtyCount));
    /// <summary>当前结果的编辑或只读说明。</summary>
    public string EditHint => Text.Get(SelectedResult?.IsEditable == true ? "MySqlInlineEditHint" : "MySqlReadOnlyResult");
    /// <summary>真实自动提交和事务状态说明。</summary>
    public string StateLabel => Text.Format("MySqlSessionStatus", Text.Get(_session?.State.Autocommit switch { true => "MySqlOn", false => "MySqlOff", _ => "MySqlUnknown" }),
        Text.Get(_session?.State.InTransaction switch { true => "MySqlTransactionActive", false => "MySqlTransactionNone", _ => "MySqlUnknown" }));
    /// <summary>已经加载的数据库名称，不伪造目录。</summary>
    public IReadOnlyList<string> Databases { get; internal set => SetProperty(ref field, value); } = [];
    /// <summary>每个语句和结果集独立保留的网格。</summary>
    public ObservableCollection<MySqlGridViewModel> Results { get; } = [];
    /// <summary>活动结果集，切换保留列宽和滚动。</summary>
    public MySqlGridViewModel? SelectedResult { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(EditHint)); OnPropertyChanged(nameof(HasResult)); } } }
    /// <summary>当前是否有结果可展示。</summary>
    public bool HasResult => SelectedResult is not null;
    /// <summary>当前对象已读取的结构元数据。</summary>
    public IReadOnlyList<MySqlObject> Structure { get; private set; } = [];
    /// <summary>数据和结构区使用公共选择器。</summary>
    public IReadOnlyList<InstanceFieldChoice> Sections { get; } = [new("data", Text.Get("MySqlData")), new("structure", Text.Get("MySqlStructure"))];
    /// <summary>当前对象正文分区。</summary>
    public string Section { get; set => SetProperty(ref field, value); } = "data";
    /// <summary>当前表页的起始偏移，不表示精确总行数。</summary>
    public int Offset { get; private set; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand ExecuteCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand SaveDocumentCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand SaveChangesCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public RelayCommand RevertCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand VerifyCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
    public AsyncRelayCommand PreviousCommand { get; }
    /// <summary>协调公共控件的操作命令，启用条件由当前状态决定。</summary>
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
    /// <summary>确认待保存输入后读取对象页。</summary>
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
    /// <summary>串行执行 SQL，保留已完成的结果和安全失败状态。</summary>
    public async Task ExecuteAsync(string sql)
    {
        if (IsBusy || !IsQuery || sql.Length == 0 || CommitInputRequested?.Invoke() == false || !await ResolveDataDraftAsync()) return;
        await RunAsync(token => ExecuteCoreAsync(sql, token));
    }
    private async Task ExecuteCoreAsync(string sql, CancellationToken token)
    {
        if (sql.Length == 0) return;
        var session = await SessionAsync(token); _executed = true;
        var execution = await session.ExecuteAsync(sql, Database, token);
        Database = session.CurrentDatabase;
        SetResults(execution.Results);
        Error = execution.Failure is null ? "" : Text.Error(execution.Failure, "MySqlCommandFailed");
        Message = string.Join(Environment.NewLine, execution.Outcomes.Select(outcome => Text.Format(outcome.Succeeded ? "MySqlStatementSucceeded" : "MySqlStatementStopped", outcome.Statement, Math.Max(0, outcome.AffectedRows))));
        Status = Text.Format("MySqlExecutionStatus", execution.Outcomes.Count, execution.Elapsed.TotalMilliseconds);
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
        foreach (string property in new[] { nameof(IsBusy), nameof(CanRun), nameof(Error), nameof(HasError), nameof(Message), nameof(HasMessage), nameof(Status), nameof(StateLabel), nameof(OutcomeUnknown), nameof(EditHint), nameof(Header), nameof(Database) }) OnPropertyChanged(property);
        foreach (var command in new[] { RefreshCommand, ExecuteCommand, SaveDocumentCommand, SaveChangesCommand, VerifyCommand, PreviousCommand, NextCommand }) command.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged(); RevertCommand.NotifyCanExecuteChanged(); DraftChanged();
    }
}
