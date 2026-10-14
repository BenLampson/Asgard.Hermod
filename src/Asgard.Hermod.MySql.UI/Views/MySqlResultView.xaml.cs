using System.Windows.Data;
using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>原生 DataGrid 同步表头和横向偏移，固定行号并虚拟化可见行列。</summary>
public partial class MySqlResultView : UserControl
{
    private bool _prepared;
    /// <summary>此网格所属的独立会话页签。</summary>
    public MySqlTabViewModel? Tab { get; set; }
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlResultView() { InitializeComponent(); DataContextChanged += (_, _) => BuildColumns(); }
    private void BuildColumns()
    {
        ResultGrid.Columns.Clear(); if (DataContext is not MySqlGridViewModel grid) return;
        foreach (var column in grid.Columns)
        {
            var display = CellTemplate(column.Ordinal, "CellDisplay"); var input = CellTemplate(column.Ordinal, "CellInput");
            var data = new DataGridTemplateColumn
            {
                Header = column,
                HeaderTemplate = (DataTemplate)Resources["ColumnHeader"],
                CellTemplate = display,
                CellEditingTemplate = input,
                Width = InitialWidth(column),
                MinWidth = 80,
                IsReadOnly = !grid.IsEditable || !column.CanUpdate || column.Generated,
                ClipboardContentBinding = new Binding($"Cells[{column.Ordinal}].CopyValue")
            };
            ResultGrid.Columns.Add(data);
        }
    }
    private DataTemplate CellTemplate(int ordinal, string key)
    {
        var presenter = new FrameworkElementFactory(typeof(ContentControl));
        presenter.SetBinding(ContentControl.ContentProperty, new Binding($"Cells[{ordinal}]"));
        presenter.SetValue(ContentControl.ContentTemplateProperty, Resources[key]); presenter.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch);
        return new DataTemplate { VisualTree = presenter };
    }
    private static double InitialWidth(MySqlColumn column) => Math.Clamp(column.ValueType == typeof(DateTime) ? 220 : column.ValueType == typeof(byte[]) ? 220
        : column.TypeName.Contains("json", StringComparison.OrdinalIgnoreCase) || column.TypeName.Contains("text", StringComparison.OrdinalIgnoreCase) ? 300
        : column.ValueType == typeof(string) ? Math.Max(180, column.Name.Length * 8 + 36) : Math.Max(110, column.Name.Length * 8 + 36), 80, 400);
    private void OnLoadingRow(object? sender, DataGridRowEventArgs args) => args.Row.Header = (args.Row.GetIndex() + (Tab?.Offset ?? 0) + 1).ToString(CultureInfo.InvariantCulture);
    private async void OnBeginningEdit(object? sender, DataGridBeginningEditEventArgs args)
    {
        if (_prepared) { _prepared = false; return; }
        args.Cancel = true;
        if (Tab is null || DataContext is not MySqlGridViewModel grid || args.Row.Item is not MySqlRowViewModel row) return;
        var cell = row.Cells[args.Column.DisplayIndex];
        if (cell.NeedsWindow) { await Tab.OpenFieldAsync(grid, row, cell); return; }
        if (!await Tab.PrepareCellAsync(grid, row, cell)) return;
        // 异步基线读取结束后再次核对当前单元格，防止焦点已移到另一行。
        if (ResultGrid.CurrentCell.Item != row || ResultGrid.CurrentCell.Column != args.Column) return;
        await Dispatcher.InvokeAsync(() =>
        {
            if (ResultGrid.CurrentCell.Item != row || ResultGrid.CurrentCell.Column != args.Column) return;
            _prepared = true; ResultGrid.BeginEdit(); _prepared = false;
        }, System.Windows.Threading.DispatcherPriority.Input);
    }
    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs args)
    {
        if (args.Row.Item is not MySqlRowViewModel row) return; var cell = row.Cells[args.Column.DisplayIndex];
        if (args.EditAction == DataGridEditAction.Cancel) { cell.Input = cell.Value is null ? "" : MySqlValues.Format(cell.Value); return; }
        try { cell.Apply(MySqlValues.Parse(cell.Input, cell.Column, false)); }
        catch (Exception error)
        {
            args.Cancel = true;
            if (FindInput(args.EditingElement) is { } input) { input.ToolTip = Text.Error(error, "MySqlFieldInvalid"); input.SetResourceReference(BorderBrushProperty, "DangerBrush"); }
        }
    }
    private static TextBox? FindInput(DependencyObject root)
    {
        if (root is TextBox input) return input;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++) if (FindInput(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }
    private void OnInputLoaded(object sender, RoutedEventArgs args) { if (sender is TextBox input) { input.Focus(); input.SelectAll(); } }
    private async void OnExpand(object sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: MySqlCellViewModel cell } || Tab is null || DataContext is not MySqlGridViewModel grid) return;
        if (!ResultGrid.CommitEdit(DataGridEditingUnit.Cell, true)) return;
        var row = FindRow((DependencyObject)sender); if (row is null) return;
        await Tab.OpenFieldAsync(grid, row, cell); ResultGrid.Focus();
    }
    private static MySqlRowViewModel? FindRow(DependencyObject? source)
    { while (source is not null) { if (source is DataGridRow { Item: MySqlRowViewModel row }) return row; source = System.Windows.Media.VisualTreeHelper.GetParent(source); } return null; }
    private async void OnOpenCurrent(object sender, RoutedEventArgs args) => await OpenCurrentAsync();
    private async Task OpenCurrentAsync()
    {
        if (Tab is null || DataContext is not MySqlGridViewModel grid || ResultGrid.CurrentCell.Item is not MySqlRowViewModel row || ResultGrid.CurrentCell.Column is not { } column) return;
        if (!ResultGrid.CommitEdit(DataGridEditingUnit.Cell, true)) return;
        await Tab.OpenFieldAsync(grid, row, row.Cells[column.DisplayIndex]); ResultGrid.Focus();
    }
    private void OnRevertCell(object sender, RoutedEventArgs args)
    {
        if (Tab is { IsBusy: false, OutcomeUnknown: false } && ResultGrid.CurrentCell.Item is MySqlRowViewModel row && ResultGrid.CurrentCell.Column is { } column) row.Cells[column.DisplayIndex].Revert();
    }
    private async void OnKeyDown(object sender, KeyEventArgs args) { if (args.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Alt) { args.Handled = true; await OpenCurrentAsync(); } }
    /// <summary>提交原生编辑器输入到本地草稿，校验失败时保留编辑。</summary>
    public bool CommitInput() => ResultGrid.CommitEdit(DataGridEditingUnit.Cell, true) && ResultGrid.CommitEdit(DataGridEditingUnit.Row, true);
}
