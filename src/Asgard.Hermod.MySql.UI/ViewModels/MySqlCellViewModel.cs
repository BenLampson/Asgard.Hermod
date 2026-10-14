namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>单元格保留原值与类型化草稿，不把截断文本写回数据库。</summary>
public sealed class MySqlCellViewModel : ObservableObject
{
    private readonly Action _changed;
    internal MySqlCellViewModel(MySqlColumn column, object? original, Action changed)
    { Column = column; Original = MySqlValues.Clone(original); Value = MySqlValues.Clone(original); _changed = changed; Input = original is null ? "" : MySqlValues.Format(original); }
    public MySqlColumn Column { get; }
    public object? Original { get; private set; }
    public object? Value { get; private set; }
    public string Input { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(IsDirty)); _changed(); } } } = "";
    public bool IsDirty => !MySqlValues.Equal(Original, Value) || Input != (Value is null ? "" : MySqlValues.Format(Value));
    public bool IsNull => Value is null;
    public bool IsNumeric => Column.ValueType != typeof(string) && Column.ValueType != typeof(byte[]) && Column.ValueType != typeof(DateTime) && Column.ValueType != typeof(TimeSpan);
    public string FullValue => Value is byte[] bytes ? Text.Format("MySqlBinaryValue", bytes.Length) : MySqlValues.Format(Value);
    public string Display => FullValue.Length > 256 ? FullValue[..256].Replace('\r', ' ').Replace('\n', ' ') + "…" : FullValue.Replace('\r', ' ').Replace('\n', ' ');
    public string CopyValue => MySqlValues.Format(Value);
    public bool NeedsWindow => Value is byte[] || FullValue.Length > 2048 || Column.TypeName.Contains("text", StringComparison.OrdinalIgnoreCase) || Column.TypeName.Equals("json", StringComparison.OrdinalIgnoreCase);
    public void Apply(object? value) { Value = MySqlValues.Clone(value); Refresh(); _changed(); }
    public void Revert() { Value = MySqlValues.Clone(Original); Refresh(); _changed(); }
    internal void Accept(object? value) { Original = MySqlValues.Clone(value); Value = MySqlValues.Clone(value); Refresh(); }
    private void Refresh()
    {
        Input = Value is null ? "" : MySqlValues.Format(Value);
        foreach (string property in new[] { nameof(Value), nameof(Original), nameof(IsDirty), nameof(IsNull), nameof(FullValue), nameof(Display), nameof(CopyValue), nameof(NeedsWindow) }) OnPropertyChanged(property);
    }
}
