using Asgard.Hermod.PostgreSql.UI.ViewModels;

namespace Asgard.Hermod.PostgreSql.UI.Views;

/// <summary>缓存各结果视图的列宽、滚动和焦点，切换页签不重建内容。</summary>
public partial class PostgreSqlTabView : UserControl
{
    private readonly Dictionary<PostgreSqlGridViewModel, PostgreSqlResultView> _views = [];
    private PostgreSqlTabViewModel? _model;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public PostgreSqlTabView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) { _model.PropertyChanged -= OnChanged; _model.InsertSqlRequested = null; } _model = null; };
    }
    private CodeEditor CurrentEditor => SqlWorkspace.Editor;
    private void Attach()
    {
        if (_model is not null) { _model.PropertyChanged -= OnChanged; _model.InsertSqlRequested = null; }
        _model = DataContext as PostgreSqlTabViewModel; if (_model is not null) { _model.PropertyChanged += OnChanged; _model.CommitInputRequested = CommitInput; _model.InsertSqlRequested = CurrentEditor.InsertText; }
        ShowResult();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(PostgreSqlTabViewModel.SelectedResult)) ShowResult(); if (args.PropertyName is nameof(PostgreSqlTabViewModel.Section) or nameof(PostgreSqlTabViewModel.Offset)) Layout(); }
    private void ShowResult()
    {
        if (_model is null) return;
        foreach (var stale in _views.Keys.Where(grid => !_model.Results.Contains(grid)).ToArray()) _views.Remove(stale);
        if (_model.SelectedResult is not { } grid) { GridHost.Content = null; SqlWorkspace.ResultContent = null; }
        else { if (!_views.TryGetValue(grid, out var view)) { view = new() { DataContext = grid, Tab = _model }; _views.Add(grid, view); } if (_model.IsQuery) SqlWorkspace.ResultContent = view; else GridHost.Content = view; }
        Layout();
    }
    private void Layout()
    {
        bool structure = _model?.Section == "structure" || _model?.IsMetadataOnly == true;
        if (_model?.IsQuery != true) EditorRow.Height = new(0);
        else if (EditorRow.Height.Value == 0) EditorRow.Height = new(210);
        EditorSplitterRow.Height = new(_model?.IsQuery == true ? 6 : 0);
        StructurePanel.Visibility = structure ? Visibility.Visible : Visibility.Collapsed; DataPanel.Visibility = structure ? Visibility.Collapsed : Visibility.Visible;
        DataActions.Visibility = structure ? Visibility.Collapsed : Visibility.Visible;
        // 空的后续页保留返回上一页；首个空页不提供无效分页。
        PageButtons.Visibility = _model?.IsQuery == false && !structure && _model.ShowPageNavigation ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnDataSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_model?.IsQuery == true) EditorRow.MaxHeight = Math.Max(80, args.NewSize.Height - 210);
    }
    /// <summary>提交原生编辑器输入到本地草稿，校验失败时保留编辑。</summary>
    public bool CommitInput() => (_model?.IsQuery == true ? SqlWorkspace.ResultContent : GridHost.Content) is not PostgreSqlResultView view || view.CommitInput();
    private async Task RunAsync(bool all)
    {
        if (_model is null || !CommitInput()) return;
        await _model.ExecuteScopeAsync(CurrentEditor.SelectedText, CurrentEditor.CaretIndex, all);
    }
    private async void OnRun(object sender, RoutedEventArgs args) => await RunAsync(false);
    private async void OnRunAll(object sender, RoutedEventArgs args) => await RunAsync(true);
    private async void OnExplain(object sender, RoutedEventArgs args)
    { if (_model is not null && CommitInput()) await _model.ExplainScopeAsync(CurrentEditor.SelectedText, CurrentEditor.CaretIndex); }
    private async void OnPageSizeChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!IsLoaded || _model is null || PageSizeSelector.SelectedItem is not int size || size == _model.PageSize) return;
        await _model.ChangePageSizeAsync(size); PageSizeSelector.SelectedItem = _model.PageSize;
    }
    private async void OnSqlTargetChanged(object sender, SelectionChangedEventArgs args)
    { if (_model is not null && SqlWorkspace.SelectedDatabase is { } database && database != _model.Database) await _model.ChangeDatabaseAsync(database); }
    private async void OnExportSqlOutput(object sender, RoutedEventArgs args)
    { if (_model is not null) await _model.ExportAsync(); }
    private void OnCopySqlOutput(object sender, RoutedEventArgs args)
    {
        if (_model?.SelectedResult?.Result is not { } result) return;
        string text = string.Join("\t", result.Columns.Select(column => column.Name)) + Environment.NewLine +
            string.Join(Environment.NewLine, result.Rows.Select(row => string.Join("\t", row.Select(value => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)))));
        try { Clipboard.SetText(text); } catch (System.Runtime.InteropServices.ExternalException) { new Asgard.Hermod.UI.Views.Dialogs.MessageDialog(Text.Get("Hermod"), Text.Get("DatabaseClipboardFailed")).ShowDialog(); }
    }
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key == Key.Enter && _model.IsQuery) { args.Handled = true; await RunAsync((Keyboard.Modifiers & ModifierKeys.Shift) != 0); }
        else if (args.Key == Key.S) { args.Handled = true; if (!CommitInput()) return; if (CurrentEditor.IsKeyboardFocusWithin) await _model.SaveDocumentAsync(); else await _model.SaveChangesCommand.ExecuteAsync(); }
    }
}
