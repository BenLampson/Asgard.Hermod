using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>只协调 WPF 视图缓存、树选择和窄窗口抽屉，不包含协议逻辑。</summary>
public partial class MySqlWorkspaceView : UserControl
{
    private readonly Dictionary<MySqlTabViewModel, MySqlTabView> _views = [];
    private MySqlWorkspaceViewModel? _model;
    private bool _compact, _drawer;
    private double _treeWidth = 240;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlWorkspaceView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = null; };
    }
    private void Attach() { if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = DataContext as MySqlWorkspaceViewModel; if (_model is not null) _model.PropertyChanged += OnModelChanged; ShowTab(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(MySqlWorkspaceViewModel.ActiveTab)) ShowTab(); }
    private void ShowTab()
    {
        if (_model is null) return;
        foreach (var stale in _views.Keys.Where(tab => !_model.Tabs.Contains(tab)).ToArray()) _views.Remove(stale);
        if (_model.ActiveTab is not { } tab) { TabHost.Content = null; return; }
        if (!_views.TryGetValue(tab, out var view)) { view = new() { DataContext = tab }; _views.Add(tab, view); }
        TabHost.Content = view; ObjectTabs.ScrollIntoView(tab);
    }
    private async void OnSelectedObject(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (_model is null || args.NewValue is not MySqlTreeNode node) return;
        if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return;
        _model.SelectedNode = node; await _model.OpenAsync(node);
        if (_compact && node.Object.Kind is MySqlObjectKind.Table or MySqlObjectKind.View or MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Event) { _drawer = false; Layout(); }
    }
    private void OnTreeRightClick(object sender, MouseButtonEventArgs args)
    {
        for (DependencyObject? item = args.OriginalSource as DependencyObject; item is not null; item = System.Windows.Media.VisualTreeHelper.GetParent(item))
            if (item is TreeViewItem { DataContext: MySqlTreeNode node }) { if (_model is not null) _model.SelectedNode = node; break; }
    }
    private void OnTabChanged(object sender, SelectionChangedEventArgs args) => ShowTab();
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool compact = args.NewSize.Width < 760; if (!_compact && compact) _treeWidth = TreeColumn.ActualWidth;
        _compact = compact; Layout();
    }
    private void OnDrawer(object sender, RoutedEventArgs args) { _drawer = !_drawer; Layout(); }
    private void Layout()
    {
        DrawerButton.Visibility = _compact ? Visibility.Visible : Visibility.Collapsed;
        TreeColumn.MinWidth = _compact ? 0 : 200; TreeColumn.Width = new(_compact ? 0 : Math.Max(200, _treeWidth)); SplitterColumn.Width = new(_compact ? 0 : 6);
        Splitter.Visibility = _compact ? Visibility.Collapsed : Visibility.Visible;
        TreePanel.Visibility = !_compact || _drawer ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(TreePanel, _compact ? 3 : 1); TreePanel.Width = _compact ? Math.Min(300, Math.Max(200, Body.ActualWidth - 40)) : double.NaN;
        TreePanel.HorizontalAlignment = _compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; Panel.SetZIndex(TreePanel, _compact ? 10 : 0);
    }
    private async void OnKnownObject(object sender, RoutedEventArgs args)
    {
        if (_model is null) return;
        var database = new TextBox { Text = _model.ActiveTab?.Database ?? "", MaxLength = 64 }; var name = new TextBox { MaxLength = 64 };
        var panel = new StackPanel { Margin = new Thickness(24) }; panel.Children.Add(new FormField { Label = Text.Get("MySqlDatabase"), Content = database, Margin = new Thickness(0, 0, 0, 14) }); panel.Children.Add(new FormField { Label = Text.Get("MySqlObjectName"), Content = name });
        var dialog = new HermodDialog { Owner = Window.GetWindow(this), Title = Text.Get("MySqlOpenKnownObject"), Width = 540, Height = 320, Content = panel };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal }; buttons.Children.Add(new Button { Content = Text.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 0, 8, 0) });
        var open = new Button { Content = Text.Get("MySqlOpenObject") }; open.SetResourceReference(StyleProperty, "PrimaryButton"); open.Click += (_, _) => { if (database.Text.Length > 0 && name.Text.Length > 0) dialog.DialogResult = true; }; buttons.Children.Add(open); dialog.FooterContent = new DialogActionBar { Content = buttons };
        if (dialog.ShowDialog() == true) await _model.OpenObjectAsync(new(MySqlObjectKind.Table, name.Text, database.Text));
    }
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || _model.Connection.IsOpen) return;
        if (args.Key == Key.Escape && _drawer) { _drawer = false; Layout(); args.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key == Key.W) { if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return; args.Handled = true; await _model.CloseTabCommand.ExecuteAsync(_model.ActiveTab); }
        if (args.Key == Key.Tab && _model.Tabs.Count > 0) { args.Handled = true; int index = _model.ActiveTab is null ? 0 : _model.Tabs.IndexOf(_model.ActiveTab); _model.ActiveTab = _model.Tabs[(index + ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1) + _model.Tabs.Count) % _model.Tabs.Count]; }
    }
}
