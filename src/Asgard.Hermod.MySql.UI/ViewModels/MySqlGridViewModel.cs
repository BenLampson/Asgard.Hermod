namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>结果集拥有独立草稿；基线读取与保存均由调用页签协调。</summary>
public sealed class MySqlGridViewModel : ObservableObject
{
    public MySqlGridViewModel(MySqlResult result)
    {
        Result = result; Rows = result.Rows.Select(row => new MySqlRowViewModel(result, row.ToArray(), Changed)).ToArray();
    }
    public MySqlResult Result { get; }
    public IReadOnlyList<MySqlColumn> Columns => Result.Columns;
    public IReadOnlyList<MySqlRowViewModel> Rows { get; }
    public bool IsEditable => Result.EditableTable is not null && !Result.IsTruncated;
    public bool IsDirty => Rows.Any(row => row.IsDirty);
    public int DirtyCount => Rows.Sum(row => row.DirtyCount);
    public string Label => Text.Format("MySqlResultTab", Result.Statement, Result.ResultIndex);
    public string Summary => Text.Format(Result.IsTruncated ? "MySqlPreviewRows" : "MySqlLoadedRows", Rows.Count, Columns.Count);
    public event Action? DraftChanged;
    public void Revert() { foreach (var row in Rows) row.Revert(); }
    private void Changed() { OnPropertyChanged(nameof(IsDirty)); OnPropertyChanged(nameof(DirtyCount)); DraftChanged?.Invoke(); }
}
