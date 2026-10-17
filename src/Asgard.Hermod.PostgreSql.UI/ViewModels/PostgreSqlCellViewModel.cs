namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>单元格保留原值与类型化草稿，不把截断文本写回数据库。</summary>
public sealed class PostgreSqlCellViewModel : ObservableObject
{
    private readonly Action _changed;
    internal PostgreSqlCellViewModel(PostgreSqlColumn column, object? original, Action changed)
    { Column = column; Original = PostgreSqlValues.Clone(original); Value = PostgreSqlValues.Clone(original); _changed = changed; Input = original is null ? "" : PostgreSqlValues.Format(original); }
    /// <summary>服务器确认的列元数据和序号。</summary>
    public PostgreSqlColumn Column { get; }
    /// <summary>读取时的原始类型化值。</summary>
    public object? Original { get; private set; }
    /// <summary>当前类型化草稿值。</summary>
    public object? Value { get; private set; }
    /// <summary>尚未提交的输入；校验失败时保留。</summary>
    public string Input { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(IsDirty)); _changed(); } } } = "";
    /// <summary>存在未应用或未提交的修改。</summary>
    public bool IsDirty => !PostgreSqlValues.Equal(Original, Value) || Input != (Value is null ? "" : PostgreSqlValues.Format(Value));
    /// <summary>SQL NULL 状态，与空字符串独立。</summary>
    public bool IsNull => Value is null;
    /// <summary>数字值使用右对齐。</summary>
    public bool IsNumeric => Column.ValueType != typeof(string) && Column.ValueType != typeof(byte[]) && Column.ValueType != typeof(DateTime) && Column.ValueType != typeof(TimeSpan);
    /// <summary>完整字段表示或二进制大小说明。</summary>
    public string FullValue => Value is byte[] bytes ? Text.Format("PostgreSqlBinaryValue", bytes.Length) : PostgreSqlValues.Format(Value);
    /// <summary>单行显示预览，不作为保存数据。</summary>
    private string DisplayValue => Value is DateTime timestamp ? timestamp.ToString("yyyy-MM-dd HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture) : Value is bool boolean ? boolean ? "true" : "false" : FullValue;
    public string Display => DisplayValue.Length > 256 ? DisplayValue[..256].Replace('\r', ' ').Replace('\n', ' ') + "…" : DisplayValue.Replace('\r', ' ').Replace('\n', ' ');
    /// <summary>复制或导出的无损原值表示。</summary>
    public string CopyValue => PostgreSqlValues.Format(Value);
    /// <summary>长文本和二进制值优先使用独立字段窗口。</summary>
    public bool NeedsWindow => Value is byte[] || FullValue.Length > 2048 || Column.TypeName.Contains("text", StringComparison.OrdinalIgnoreCase) || Column.TypeName.Equals("json", StringComparison.OrdinalIgnoreCase);
    /// <summary>应用类型化值到本地草稿，不直接写数据库。</summary>
    public void Apply(object? value) { Value = PostgreSqlValues.Clone(value); Refresh(); _changed(); }
    /// <summary>还原本地草稿，不撤销已经提交的数据库写入。</summary>
    public void Revert() { Value = PostgreSqlValues.Clone(Original); Refresh(); _changed(); }
    internal void Accept(object? value) { Original = PostgreSqlValues.Clone(value); Value = PostgreSqlValues.Clone(value); Refresh(); }
    private void Refresh()
    {
        Input = Value is null ? "" : PostgreSqlValues.Format(Value);
        foreach (string property in new[] { nameof(Value), nameof(Original), nameof(IsDirty), nameof(IsNull), nameof(FullValue), nameof(Display), nameof(CopyValue), nameof(NeedsWindow) }) OnPropertyChanged(property);
    }
}
