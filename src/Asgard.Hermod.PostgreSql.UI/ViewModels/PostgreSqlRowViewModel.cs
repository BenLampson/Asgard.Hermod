namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>只为访问过的列创建视图模型，让多列虚拟化不提前实例化全部单元格。</summary>
public sealed class PostgreSqlRowViewModel
{
    private readonly Dictionary<int, PostgreSqlCellViewModel> _cells = [];
    private readonly PostgreSqlResult _result;
    private readonly object?[] _original;
    private readonly Action _changed;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlRowViewModel(PostgreSqlResult result, object?[] original, Action changed) { _result = result; _original = original; _changed = changed; Cells = new(this); }
    /// <summary>按需创建的单元格索引器，避免提前实例化所有列。</summary>
    public PostgreSqlCells Cells { get; }
    /// <summary>保存前比较的完整服务器原行。</summary>
    public IReadOnlyDictionary<string, object?>? Baseline { get; internal set; }
    /// <summary>存在未应用或未提交的修改。</summary>
    public bool IsDirty => _cells.Values.Any(cell => cell.IsDirty);
    /// <summary>待提交字段数量。</summary>
    public int DirtyCount => _cells.Values.Count(cell => cell.IsDirty);
    /// <summary>仅枚举已经访问过的单元格。</summary>
    public IEnumerable<PostgreSqlCellViewModel> ExistingCells => _cells.Values;
    /// <summary>结果集中已读取的原始物理列值。</summary>
    public IReadOnlyDictionary<string, object?> OriginalProjection => _result.Columns.Where(column => column.BaseName.Length > 0)
        .DistinctBy(column => column.BaseName).ToDictionary(column => column.BaseName, column => _original[column.Ordinal], StringComparer.Ordinal);
    /// <summary>保留完整基线的本地行草稿。</summary>
    public PostgreSqlRowChange Change => new(Baseline ?? throw Text.Exception("PostgreSqlReadOnlyResult"),
        _cells.Values.Where(cell => cell.IsDirty).ToDictionary(cell => cell.Column.BaseName, cell => PostgreSqlValues.Clone(cell.Value), StringComparer.Ordinal));
    /// <summary>还原本地草稿，不撤销已经提交的数据库写入。</summary>
    public void Revert() { foreach (var cell in _cells.Values) cell.Revert(); }
    internal void Accept(IReadOnlyDictionary<string, object?> current)
    {
        Baseline = current;
        foreach (var column in _result.Columns)
        {
            if (!current.TryGetValue(column.BaseName, out var value)) continue;
            _original[column.Ordinal] = PostgreSqlValues.Clone(value);
            if (_cells.TryGetValue(column.Ordinal, out var cell)) cell.Accept(value);
        }
        _changed();
    }
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public sealed class PostgreSqlCells(PostgreSqlRowViewModel row)
    {
        public PostgreSqlCellViewModel this[int index]
        {
            get
            {
                if (!row._cells.TryGetValue(index, out var cell)) { cell = new(row._result.Columns[index], row._original[index], row._changed); row._cells.Add(index, cell); }
                return cell;
            }
        }
    }
}
