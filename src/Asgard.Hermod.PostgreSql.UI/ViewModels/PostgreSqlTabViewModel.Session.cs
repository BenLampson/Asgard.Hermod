using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

public sealed partial class PostgreSqlTabViewModel
{
    /// <summary>保留当前 Session 的对象文档，切换活动对象不重新生成集合。</summary>
    public ObservableCollection<PostgreSqlTabViewModel> Documents { get; } = [];
    /// <summary>安全公开端点，不存储连接字符串或凭据。</summary>
    public string Endpoint { get; internal set; } = "";
    /// <summary>对象视图归属于脚本 Session；AI 使用所属 Session。</summary>
    public PostgreSqlTabViewModel? SessionOwner { get; internal set; }
    /// <summary>仅脚本 Session 拥有可变范围。</summary>
    public DatabaseSessionScope? Scope { get; private set; }
    /// <summary>正文脚本标题不与顶层 Session 标题重复。</summary>
    public string DocumentTitle => IsQuery ? Text.Get("DatabaseSessionScript") : Name;
    /// <summary>历史仅属于当前脚本。</summary>
    public bool IsHistoryOpen { get => IsHistoryOutput; set => OutputView = value ? "history" : "results"; }
    private long _resultScopeRevision;
    /// <summary>旧范围结果保留在界面，但不能作为新范围的 AI 样本。</summary>
    public bool CanShareResults => SelectedResult is not null && (Scope is null || _resultScopeRevision == ScopeRevision);
    /// <summary>当前范围版本用于拒绝旧模型建议。</summary>
    public long ScopeRevision => Scope?.Revision ?? 0;
    /// <summary>唯一数据库固定呈现。</summary>
    public bool HasSingleDatabase => Scope?.HasSingleDatabase ?? Databases.Count == 1;
    /// <summary>多个候选才显示执行目标选择器。</summary>
    public bool HasMultipleDatabases => !HasSingleDatabase;
    /// <summary>确认和连接保护期间阻止新的执行。</summary>
    public bool IsManagingScope { get; internal set { if (SetProperty(ref field, value)) Refresh(); } }
    /// <summary>创建范围不通过树选择或服务器默认库推断目标。</summary>
    internal void InitializeScope(DatabaseSessionScope scope)
    {
        if (!IsQuery || Scope is not null) throw new InvalidOperationException();
        Scope = scope; Database = scope.ExecutionTarget;
        scope.PropertyChanged += OnScopeChanged; SynchronizeScope();
    }
    private void OnScopeChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(DatabaseSessionScope.Revision)) SynchronizeScope(); }
    private void SynchronizeScope()
    {
        if (Scope is null) return;
        Databases = Scope.Databases.Select(item => item.Name).ToArray(); _completionMetadata.Clear();
        OnPropertyChanged(nameof(ScopeRevision)); OnPropertyChanged(nameof(CanShareResults)); OnPropertyChanged(nameof(HasSingleDatabase)); OnPropertyChanged(nameof(HasMultipleDatabases)); Refresh();
    }
    /// <summary>已确认且完成连接保护后提交范围；不删除实际数据库。</summary>
    internal void CommitScopeRemoval(DatabaseRemovalRequest request, DatabaseRemovalDecision decision)
    {
        if (Scope?.ApplyConfirmedRemoval(request, decision) == true) { Database = Scope.ExecutionTarget; Refresh(); }
    }
    /// <summary>整批确认完成后才改变范围与执行目标。</summary>
    internal bool CommitScopeChange(DatabaseScopeChange change, string target)
    {
        if (Scope?.ApplyConfirmedChange(change, target) != true) return false;
        Database = Scope.ExecutionTarget; Refresh(); return true;
    }
    /// <summary>移除内容先确认，取消不修改原内容。</summary>
    internal Task<bool> ConfirmRemovalAsync(string actionKey) => _dialogs.ConfirmSessionActionAsync(Instance?.Name ?? "", IsQuery ? Text.Format("DatabaseSessionActionContext", Name, string.Join(", ", Databases)) : Text.Format("DatabaseSessionObjectActionContext", SessionOwner?.Name ?? Name, Header), actionKey, Endpoint);
}
