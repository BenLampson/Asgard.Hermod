namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>结果集拥有独立草稿；基线读取与保存均由调用页签协调。</summary>
public sealed class MySqlGridViewModel : ObservableObject
{
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlGridViewModel(MySqlResult result)
    {
        Result = result; Rows = result.Rows.Select(row => new MySqlRowViewModel(result, row.ToArray(), Changed)).ToArray();
    }
    /// <summary>保留原值、顺序和完整性标志的协议结果。</summary>
    public MySqlResult Result { get; }
    /// <summary>原始结果列顺序，允许同名列按序号区分。</summary>
    public IReadOnlyList<MySqlColumn> Columns => Result.Columns;
    /// <summary>已加载的有界结果行。</summary>
    public IReadOnlyList<MySqlRowViewModel> Rows { get; }
    /// <summary>是否满足当前完整结果和编辑权限条件。</summary>
    public bool IsEditable => Result.EditableTable is not null && !Result.IsTruncated;
    /// <summary>存在未应用或未提交的修改。</summary>
    public bool IsDirty => Rows.Any(row => row.IsDirty);
    /// <summary>待提交字段数量。</summary>
    public int DirtyCount => Rows.Sum(row => row.DirtyCount);
    /// <summary>供公共控件展示的已本地化标题。</summary>
    public string Label => Text.Format("MySqlResultTab", Result.Statement, Result.ResultIndex);
    /// <summary>已加载行列及完整性的本地化说明。</summary>
    public string Summary => Text.Format(Result.IsTruncated ? "MySqlPreviewRows" : "MySqlLoadedRows", Rows.Count, Columns.Count);
    /// <summary>草稿变化时通知页签更新保存和关闭状态。</summary>
    public event Action? DraftChanged;
    /// <summary>还原本地草稿，不撤销已经提交的数据库写入。</summary>
    public void Revert() { foreach (var row in Rows) row.Revert(); }
    private void Changed() { OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(DirtyCount)); DraftChanged?.Invoke(); }
}
