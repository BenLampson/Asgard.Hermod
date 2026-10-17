namespace Asgard.Hermod.UI.ViewModels;

/// <summary>计划节点的本地阅读状态；展开与选择不持有数据库命令。</summary>
public sealed class DatabasePlanNodeViewModel : ObservableObject
{
    private readonly Action _changed;
    internal DatabasePlanNodeViewModel(DatabasePlanNode node, DatabasePlanNodeViewModel? parent, Action changed)
    {
        Node = node; Parent = parent; _changed = changed;
        Children = node.Children.Select(child => new DatabasePlanNodeViewModel(child, this, changed)).ToArray();
        ToggleCommand = new RelayCommand(_ => IsExpanded = !IsExpanded, _ => Children.Count > 0);
    }
    /// <summary>原始不可变节点。</summary>
    public DatabasePlanNode Node { get; }
    /// <summary>仅由服务器关系决定的父节点。</summary>
    public DatabasePlanNodeViewModel? Parent { get; }
    /// <summary>服务器顺序中的子节点。</summary>
    public IReadOnlyList<DatabasePlanNodeViewModel> Children { get; }
    /// <summary>当前展开状态。</summary>
    public bool IsExpanded { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(ExpanderIcon)); _changed(); } } } = true;
    /// <summary>是否有真实子节点。</summary>
    public bool HasChildren => Children.Count > 0;
    /// <summary>逐层缩进只影响步骤列。</summary>
    public Thickness Indent => new(Depth * 24, 0, 0, 0);
    /// <summary>树中的深度。</summary>
    public int Depth => Parent is null ? 0 : Parent.Depth + 1;
    /// <summary>展开图标。</summary>
    public string ExpanderIcon => !HasChildren ? "corner-down-right" : IsExpanded ? "chevron-down" : "chevron-right";
    /// <summary>算子图标仅描述访问方式，不表达未经证实的性能风险。</summary>
    public string Icon => Node.Operation switch
    {
        DatabasePlanOperation.Sort => "arrow-down-wide-narrow",
        DatabasePlanOperation.Filter => "list-filter",
        DatabasePlanOperation.TableScan or DatabasePlanOperation.SequentialScan or DatabasePlanOperation.IndexScan or DatabasePlanOperation.IndexRangeScan or DatabasePlanOperation.IndexLookup => "table-2",
        _ => "git-branch"
    };
    /// <summary>明确识别的算子使用共享翻译，未知算子原名直接保留。</summary>
    public string Name => Node.Operation == DatabasePlanOperation.Unknown ? Node.ServerOperator : Text.Get("PlanOperator" + Node.Operation);
    /// <summary>服务器算子原名。</summary>
    public string ServerName => Node.ServerOperator;
    /// <summary>完整对象或条件，不截断快照。</summary>
    public string Expression => Node.Expression;
    /// <summary>实际行数；估算阅读器改用独立估算列。</summary>
    public string ActualRows => Number(Node.Metrics.ActualRows);
    /// <summary>估算行数。</summary>
    public string EstimatedRows => Number(Node.Metrics.EstimatedRows);
    /// <summary>首行与完成时间分开保留，未执行不伪装为零。</summary>
    public string ActualTime => Node.Metrics.NeverExecuted ? Text.Get("PlanNeverExecuted") : Text.Format("PlanNumberPair", Number(Node.Metrics.FirstRowMilliseconds), Number(Node.Metrics.CompletionMilliseconds));
    /// <summary>成本不带时间单位。</summary>
    public string Cost => Number(Node.Metrics.TotalCost);
    /// <summary>估算行宽，单位字节；未提供时仍为缺失。</summary>
    public string EstimatedWidth => Number(Node.Metrics.EstimatedWidth);
    /// <summary>循环次数不与行数或时间相乘。</summary>
    public string Loops => Number(Node.Metrics.Loops);
    /// <summary>只改变本地树的展开状态。</summary>
    public RelayCommand ToggleCommand { get; }
    internal static string Number(string? value) => value ?? Text.Get("PlanMissingMetric");
}
