namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>独立字段窗口应用到表格草稿，关闭窗口不提交数据库。</summary>
public sealed class PostgreSqlFieldEditorViewModel : ObservableObject, IDisposable
{
    private readonly PostgreSqlCellViewModel _cell;
    private readonly string _context;
    private readonly InstanceItemViewModel? _instance;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlFieldEditorViewModel(PostgreSqlCellViewModel cell, string context, bool editable, InstanceItemViewModel? instance = null)
    {
        _cell = cell; _context = context; _instance = instance; IsEditable = editable; IsNull = cell.Value is null;
        if (_instance is not null) _instance.PropertyChanged += OnInstanceMetadataChanged;
        Input = cell.Value is null ? "" : PostgreSqlValues.Format(cell.Value); Format = IsBinary ? "hex" : "text"; Validate();
    }
    /// <summary>窗口中展示的完整对象和行身份。</summary>
    public string Context => _instance is null ? _context : Text.Format("PostgreSqlFieldOperationTarget", _instance.Name, _instance.EnvironmentLabel, _context);
    private void OnInstanceMetadataChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(InstanceItemViewModel.Name) or nameof(InstanceItemViewModel.EnvironmentLabel)) OnPropertyChanged(nameof(Context));
    }
    /// <inheritdoc />
    public void Dispose() { if (_instance is not null) _instance.PropertyChanged -= OnInstanceMetadataChanged; }
    /// <summary>对象原始名称或已本地化的文稿标题。</summary>
    public string Name => _cell.Column.Name;
    /// <summary>服务器提供的字段类型名称。</summary>
    public string TypeName => _cell.Column.TypeName;
    /// <summary>是否满足当前完整结果和编辑权限条件。</summary>
    public bool IsEditable { get; }
    /// <summary>是否需要无损二进制编辑格式。</summary>
    public bool IsBinary => _cell.Column.ValueType == typeof(byte[]);
    /// <summary>是否允许在此窗口设置 SQL NULL。</summary>
    public bool AllowsNull => IsEditable && _cell.Column.Nullable;
    /// <summary>NULL 状态下不允许输入其他值。</summary>
    public bool EditorEnabled => !IsNull;
    /// <summary>窗口操作含义的完整本地化说明。</summary>
    public string Help => Text.Get(IsEditable ? "PostgreSqlFieldApplyHelp" : "PostgreSqlReadOnlyResult");
    /// <summary>尚未提交的输入；校验失败时保留。</summary>
    public string Input { get; set { if (SetProperty(ref field, value)) Validate(); } } = "";
    /// <summary>SQL NULL 状态，与空字符串独立。</summary>
    public bool IsNull { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(EditorEnabled)); Validate(); } } }
    public string Format
    {
        get; set
        {
            if (field == value) return;
            if (field is not null && IsBinary && Input.Length > 0)
            {
                try { var bytes = (byte[])PostgreSqlValues.Parse(Input, _cell.Column, false, field)!; Input = value == "base64" ? Convert.ToBase64String(bytes) : Convert.ToHexString(bytes); }
                catch (Exception) { OnPropertyChanged(); return; }
            }
            field = value; OnPropertyChanged(); Validate();
        }
    } = "text";
    /// <summary>使用稳定标识的无损格式选项。</summary>
    public IReadOnlyList<InstanceFieldChoice> Formats { get; } = [new("hex", "Hex"), new("base64", "Base64")];
    /// <summary>可安全展示的本地化错误。</summary>
    public string Error { get; private set; } = "";
    /// <summary>当前是否有需要呈现的错误。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>输入有效且具有编辑资格时允许应用。</summary>
    public bool CanApply => IsEditable && !HasError;
    /// <summary>存在未应用或未提交的修改。</summary>
    public bool IsDirty => IsNull != (_cell.Value is null) || Input != (_cell.Value is null ? "" : IsBinary && Format == "base64" ? Convert.ToBase64String((byte[])_cell.Value) : PostgreSqlValues.Format(_cell.Value));
    private void Validate()
    {
        if (_cell is null) return;
        Error = "";
        if (IsEditable) try { PostgreSqlValues.Parse(Input, _cell.Column, IsNull, Format); } catch (Exception error) { Error = Text.Error(error, "PostgreSqlFieldInvalid"); }
        OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(IsDirty));
    }
    /// <summary>应用类型化值到本地草稿，不直接写数据库。</summary>
    public bool Apply()
    {
        Validate(); if (!CanApply) return false; _cell.Apply(PostgreSqlValues.Parse(Input, _cell.Column, IsNull, Format)); return true;
    }
}
