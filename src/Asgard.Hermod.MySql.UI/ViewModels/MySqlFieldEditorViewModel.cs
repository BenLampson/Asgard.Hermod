namespace Asgard.Hermod.MySql.UI.ViewModels;

/// <summary>独立字段窗口应用到表格草稿，关闭窗口不提交数据库。</summary>
public sealed class MySqlFieldEditorViewModel : ObservableObject
{
    private readonly MySqlCellViewModel _cell;
    public MySqlFieldEditorViewModel(MySqlCellViewModel cell, string context, bool editable)
    {
        _cell = cell; Context = context; IsEditable = editable; IsNull = cell.Value is null;
        Input = cell.Value is null ? "" : MySqlValues.Format(cell.Value); Format = IsBinary ? "hex" : "text"; Validate();
    }
    public string Context { get; }
    public string Name => _cell.Column.Name;
    public string TypeName => _cell.Column.TypeName;
    public bool IsEditable { get; }
    public bool IsBinary => _cell.Column.ValueType == typeof(byte[]);
    public bool AllowsNull => IsEditable && _cell.Column.Nullable;
    public bool EditorEnabled => !IsNull;
    public string Help => Text.Get(IsEditable ? "MySqlFieldApplyHelp" : "MySqlReadOnlyResult");
    public string Input { get; set { if (SetProperty(ref field, value)) Validate(); } } = "";
    public bool IsNull { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(EditorEnabled)); Validate(); } } }
    public string Format
    {
        get; set
        {
            if (field == value) return;
            if (field is not null && IsBinary && Input.Length > 0)
            {
                try { var bytes = (byte[])MySqlValues.Parse(Input, _cell.Column, false, field)!; Input = value == "base64" ? Convert.ToBase64String(bytes) : Convert.ToHexString(bytes); }
                catch (Exception) { OnPropertyChanged(); return; }
            }
            field = value; OnPropertyChanged(); Validate();
        }
    } = "text";
    public IReadOnlyList<InstanceFieldChoice> Formats { get; } = [new("hex", "Hex"), new("base64", "Base64")];
    public string Error { get; private set; } = "";
    public bool HasError => Error.Length > 0;
    public bool CanApply => IsEditable && !HasError;
    public bool IsDirty => IsNull != (_cell.Value is null) || Input != (_cell.Value is null ? "" : IsBinary && Format == "base64" ? Convert.ToBase64String((byte[])_cell.Value) : MySqlValues.Format(_cell.Value));
    private void Validate()
    {
        if (_cell is null) return;
        Error = "";
        if (IsEditable) try { MySqlValues.Parse(Input, _cell.Column, IsNull, Format); } catch (Exception error) { Error = Text.Error(error, "MySqlFieldInvalid"); }
        OnPropertyChanged(nameof(Error)); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(CanApply)); OnPropertyChanged(nameof(IsDirty));
    }
    public bool Apply()
    {
        Validate(); if (!CanApply) return false; _cell.Apply(MySqlValues.Parse(Input, _cell.Column, IsNull, Format)); return true;
    }
}
