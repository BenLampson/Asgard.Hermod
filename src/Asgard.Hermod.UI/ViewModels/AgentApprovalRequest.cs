namespace Asgard.Hermod.UI.ViewModels;

/// <summary>一张不可变目标的单次确认卡；取消与迟到点击都不能恢复执行资格。</summary>
public sealed class AgentApprovalRequest : ObservableObject
{
    private readonly TaskCompletionSource<bool> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationToken _token;
    private readonly string _title;
    private bool _settled;
    /// <summary>代码与来源在请求时冻结，确认不授予后续操作的权限。</summary>
    public AgentApprovalRequest(string title, string target, string code, CancellationToken token, Action? insert = null)
    {
        _title = title; Target = target; Code = code; _token = token;
        ApproveCommand = new RelayCommand(_ => Complete(true, "ChatActionApproved"), _ => IsPending);
        RejectCommand = new RelayCommand(_ => Complete(false, "ChatActionRejected"), _ => IsPending);
        InsertCommand = new RelayCommand(_ => { if (!IsPending || insert is null) return; insert(); Complete(false, "ChatActionInserted"); }, _ => IsPending && insert is not null);
        CanInsert = insert is not null;
    }
    /// <summary>本地化的操作类别。</summary>
    public string Title => IsPending ? _title : Text.Get("ChatReviewedOperation");
    /// <summary>实例、Session、地址与实际执行目标，不包含连接凭据。</summary>
    public string Target { get; }
    /// <summary>用户需要检查的完整 SQL 或命令，不截断。</summary>
    public string Code { get; }
    /// <summary>取消令牌一旦失效，点击立即失去资格。</summary>
    public bool IsPending => !_settled && !_token.IsCancellationRequested;
    /// <summary>SQL 可只填入编辑器并拒绝本次执行。</summary>
    public bool CanInsert { get; }
    /// <summary>安全的确认结果。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>只批准这一条已展示的操作。</summary>
    public RelayCommand ApproveCommand { get; }
    /// <summary>拒绝本次执行。</summary>
    public RelayCommand RejectCommand { get; }
    /// <summary>只填入原来源编辑器，不自动执行。</summary>
    public RelayCommand InsertCommand { get; }
    /// <summary>可取消等待，不依赖任何原生对话框；调用者仍须复核连接和范围。</summary>
    public async Task<bool> WaitAsync()
    {
        try { return await _completion.Task.WaitAsync(_token); }
        catch (OperationCanceledException) { Complete(false, "ChatActionCanceled"); throw; }
    }
    private void Complete(bool approved, string statusKey)
    {
        if (_settled) return;
        if (_token.IsCancellationRequested) { approved = false; statusKey = "ChatActionCanceled"; }
        _settled = true; Status = Text.Get(statusKey); _completion.TrySetResult(approved);
        OnPropertyChanged(nameof(IsPending)); OnPropertyChanged(nameof(Title)); ApproveCommand.NotifyCanExecuteChanged(); RejectCommand.NotifyCanExecuteChanged(); InsertCommand.NotifyCanExecuteChanged();
    }
}
