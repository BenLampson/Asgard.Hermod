namespace Asgard.Hermod.UI.Controls;

/// <summary>只读字段目录；选择与滚动仅使用已经返回的元数据，不重新读取服务器。</summary>
public partial class DatabaseColumnReader : UserControl
{
    /// <summary>当前对象已读取的字段。</summary>
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(nameof(Rows), typeof(IEnumerable<DatabaseColumnRow>), typeof(DatabaseColumnReader), new PropertyMetadata(null, (sender, _) => ((DatabaseColumnReader)sender).RefreshRows()));
    /// <summary>字段快照，不解析 SQL 或本地化详情。</summary>
    public IEnumerable<DatabaseColumnRow>? Rows { get => (IEnumerable<DatabaseColumnRow>?)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    /// <summary>构造纯阅读界面，不持有协议连接。</summary>
    public DatabaseColumnReader() { InitializeComponent(); RefreshRows(); }
    private void RefreshRows()
    {
        if (ColumnGrid is null) return;
        string? selected = (ColumnGrid.SelectedItem as DatabaseColumnRow)?.Name;
        var rows = Rows?.ToArray() ?? []; ColumnGrid.ItemsSource = rows;
        ColumnGrid.SelectedItem = rows.FirstOrDefault(row => row.Name == selected) ?? rows.FirstOrDefault(); ShowSelection();
    }
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs args) => ShowSelection();
    private void ShowSelection()
    {
        if (SelectedName is null) return;
        var row = ColumnGrid.SelectedItem as DatabaseColumnRow;
        SelectedName.Text = row?.Name ?? Text.Get("DatabaseColumnSelect");
        SelectedComment.Text = row?.Comment is { Length: > 0 } comment ? comment : Text.Get("DatabaseColumnOriginalHint");
        SelectedDefinition.Text = row?.Detail ?? "";
    }
    private void OnSizeChanged(object sender, SizeChangedEventArgs args) => UpdateMaximumHeight();
    private void OnSelectionSizeChanged(object sender, SizeChangedEventArgs args) => UpdateMaximumHeight();
    private void UpdateMaximumHeight() { if (ColumnGrid is not null && SelectionCard is not null) ColumnGrid.MaxHeight = Math.Max(80, ActualHeight - SelectionCard.ActualHeight - 16); }
}
