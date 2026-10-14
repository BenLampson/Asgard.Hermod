namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>只为访问过的列创建视图模型，让多列虚拟化不提前实例化全部单元格。</summary>
public sealed class MySqlRowViewModel
{
    private readonly Dictionary<int, MySqlCellViewModel> _cells = [];
    private readonly MySqlResult _result;
    private readonly object?[] _original;
    private readonly Action _changed;
    public MySqlRowViewModel(MySqlResult result, object?[] original, Action changed) { _result = result; _original = original; _changed = changed; Cells = new(this); }
    public MySqlCells Cells { get; }
    public IReadOnlyDictionary<string, object?>? Baseline { get; internal set; }
    public bool IsDirty => _cells.Values.Any(cell => cell.IsDirty);
    public int DirtyCount => _cells.Values.Count(cell => cell.IsDirty);
    public IEnumerable<MySqlCellViewModel> ExistingCells => _cells.Values;
    public IReadOnlyDictionary<string, object?> OriginalProjection => _result.Columns.Where(column => column.BaseName.Length > 0)
        .DistinctBy(column => column.BaseName).ToDictionary(column => column.BaseName, column => _original[column.Ordinal], StringComparer.Ordinal);
    public MySqlRowChange Change => new(Baseline ?? throw Text.Exception("MySqlReadOnlyResult"),
        _cells.Values.Where(cell => cell.IsDirty).ToDictionary(cell => cell.Column.BaseName, cell => MySqlValues.Clone(cell.Value), StringComparer.Ordinal));
    public void Revert() { foreach (var cell in _cells.Values) cell.Revert(); }
    internal void Accept(IReadOnlyDictionary<string, object?> current)
    {
        Baseline = current;
        foreach (var column in _result.Columns)
        {
            if (!current.TryGetValue(column.BaseName, out var value)) continue;
            _original[column.Ordinal] = MySqlValues.Clone(value);
            if (_cells.TryGetValue(column.Ordinal, out var cell)) cell.Accept(value);
        }
        _changed();
    }
    public sealed class MySqlCells(MySqlRowViewModel row)
    {
        public MySqlCellViewModel this[int index]
        {
            get
            {
                if (!row._cells.TryGetValue(index, out var cell)) { cell = new(row._result.Columns[index], row._original[index], row._changed); row._cells.Add(index, cell); }
                return cell;
            }
        }
    }
}
