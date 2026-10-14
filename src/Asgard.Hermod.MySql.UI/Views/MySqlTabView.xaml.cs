using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>缓存各结果视图的列宽、滚动和焦点，切换页签不重建内容。</summary>
public partial class MySqlTabView : UserControl
{
    private readonly Dictionary<MySqlGridViewModel, MySqlResultView> _views = [];
    private MySqlTabViewModel? _model;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlTabView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnChanged; _model = null; };
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= OnChanged;
        _model = DataContext as MySqlTabViewModel; if (_model is not null) { _model.PropertyChanged += OnChanged; _model.CommitInputRequested = CommitInput; }
        ShowResult();
    }
    private void OnChanged(object? sender, PropertyChangedEventArgs args)
    { if (args.PropertyName == nameof(MySqlTabViewModel.SelectedResult)) ShowResult(); if (args.PropertyName == nameof(MySqlTabViewModel.Section)) Layout(); }
    private void ShowResult()
    {
        if (_model is null) return;
        foreach (var stale in _views.Keys.Where(grid => !_model.Results.Contains(grid)).ToArray()) _views.Remove(stale);
        if (_model.SelectedResult is not { } grid) GridHost.Content = null;
        else { if (!_views.TryGetValue(grid, out var view)) { view = new() { DataContext = grid, Tab = _model }; _views.Add(grid, view); } GridHost.Content = view; }
        Layout();
    }
    private void Layout()
    {
        bool structure = _model?.Section == "structure" || _model?.IsMetadataOnly == true;
        if (_model?.IsQuery != true) EditorRow.Height = new(0);
        else if (EditorRow.Height.Value == 0) EditorRow.Height = new(210);
        EditorSplitterRow.Height = new(_model?.IsQuery == true ? 6 : 0);
        StructurePanel.Visibility = structure ? Visibility.Visible : Visibility.Collapsed; DataPanel.Visibility = structure ? Visibility.Collapsed : Visibility.Visible;
        PageButtons.Visibility = _model?.IsQuery == false && !structure ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnDataSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (_model?.IsQuery == true) EditorRow.MaxHeight = Math.Max(80, args.NewSize.Height - 210);
    }
    /// <summary>提交原生编辑器输入到本地草稿，校验失败时保留编辑。</summary>
    public bool CommitInput() => GridHost.Content is not MySqlResultView view || view.CommitInput();
    private async Task RunAsync(bool all)
    {
        if (_model is null || !CommitInput()) return;
        await _model.ExecuteScopeAsync(SqlEditor.SelectedText, SqlEditor.CaretIndex, all);
    }
    private async void OnRun(object sender, RoutedEventArgs args) => await RunAsync(false);
    private void OnRunMenu(object sender, RoutedEventArgs args)
    {
        var menu = new ContextMenu(); var current = new MenuItem { Header = Text.Get("MySqlRunCurrent") }; var all = new MenuItem { Header = Text.Get("MySqlRunAll") };
        current.Click += async (_, _) => await RunAsync(false); all.Click += async (_, _) => await RunAsync(true); menu.Items.Add(current); menu.Items.Add(all); menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
    }
    private async void OnDatabaseChanged(object sender, SelectionChangedEventArgs args)
    {
        if (!IsLoaded || _model is null || DatabaseSelector.SelectedItem is not string database || database == _model.Database) return;
        await _model.ChangeDatabaseAsync(database); DatabaseSelector.Text = _model.Database;
    }
    private async void OnDatabaseKeyDown(object sender, KeyEventArgs args)
    { if (args.Key == Key.Enter && _model is not null) { args.Handled = true; await _model.ChangeDatabaseAsync(DatabaseSelector.Text); DatabaseSelector.Text = _model.Database; } }
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key == Key.Enter && _model.IsQuery) { args.Handled = true; await RunAsync((Keyboard.Modifiers & ModifierKeys.Shift) != 0); }
        else if (args.Key == Key.S) { args.Handled = true; if (!CommitInput()) return; if (SqlEditor.IsKeyboardFocusWithin) await _model.SaveDocumentAsync(); else await _model.SaveChangesCommand.ExecuteAsync(); }
    }
}
