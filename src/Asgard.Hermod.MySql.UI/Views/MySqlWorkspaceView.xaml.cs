using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>只协调 WPF 视图缓存、树选择和窄窗口抽屉，不包含协议逻辑。</summary>
public partial class MySqlWorkspaceView : UserControl
{
    private readonly Dictionary<MySqlTabViewModel, MySqlTabView> _views = [];
    private MySqlWorkspaceViewModel? _model;
    private bool _compact, _drawer;
    private bool _treeVisible = true;
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
        // 分屏收起时保留同一助手视图与会话，只更新当前 Session 的绑定。
        AssistantPanel.DataContext = _model?.Assistant;
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
        _model.SelectedNode = node;
        if (node.Object.Kind != MySqlObjectKind.Database) await _model.OpenAsync(node);
        if (_compact && node.Object.Kind is MySqlObjectKind.Table or MySqlObjectKind.View or MySqlObjectKind.Procedure or MySqlObjectKind.Function or MySqlObjectKind.Event) { _drawer = false; Layout(); }
    }
    /// <summary>鼠标再次点击已选数据库仍可唤回窗口；展开按钮和键盘方向键只负责导航。</summary>
    private void OnObjectClick(object sender, MouseButtonEventArgs args)
    {
        if (_model is null || _model.Connection.IsOpen) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is ButtonBase) return;
            if (source is not TreeViewItem item) continue;
            if (item.DataContext is MySqlTreeNode { Object.Kind: MySqlObjectKind.Database } node && ReferenceEquals(_model.SelectedNode, node))
            { if (_model.IsCatalog) _model.OpenDatabase(node.Object.Name); else node.IsExpanded = !node.IsExpanded; args.Handled = true; }
            return;
        }
    }
    private void OnTabChanged(object sender, SelectionChangedEventArgs args) => ShowTab();
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        bool compact = args.NewSize.Width < 760; if (!_compact && compact && _treeVisible) _treeWidth = TreeColumn.ActualWidth;
        _compact = compact; Layout();
    }
    private void OnDrawer(object sender, RoutedEventArgs args)
    {
        if (_compact) _drawer = !_drawer;
        else { if (_treeVisible) _treeWidth = TreeColumn.ActualWidth; _treeVisible = !_treeVisible; }
        Layout();
    }
    private void Layout()
    {
        bool docked = !_compact && _treeVisible;
        DrawerButton.Visibility = docked || _compact && _drawer ? Visibility.Collapsed : Visibility.Visible;
        TreeColumn.MinWidth = docked ? 200 : 0; TreeColumn.Width = new(docked ? Math.Max(200, _treeWidth) : 0); SplitterColumn.Width = new(docked ? 6 : 0);
        Splitter.Visibility = docked ? Visibility.Visible : Visibility.Collapsed;
        TreePanel.Visibility = docked || _compact && _drawer ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumnSpan(TreePanel, _compact ? 3 : 1); TreePanel.Width = _compact ? Math.Min(300, Math.Max(200, Body.ActualWidth - 40)) : double.NaN;
        TreePanel.HorizontalAlignment = _compact ? HorizontalAlignment.Left : HorizontalAlignment.Stretch; Panel.SetZIndex(TreePanel, _compact ? 10 : 0);
    }
    private async void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (_model is null || _model.Connection.IsOpen) return;
        if (args.Key == Key.Enter && ObjectTree.IsKeyboardFocusWithin && _model.SelectedNode is { Object.Kind: MySqlObjectKind.Database } database)
        { args.Handled = true; await _model.OpenAsync(database); return; }
        if (args.Key == Key.Escape && _drawer) { _drawer = false; Layout(); args.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key == Key.W) { if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return; args.Handled = true; await _model.CloseTabCommand.ExecuteAsync(_model.ActiveTab); }
        if (args.Key == Key.Tab && _model.Tabs.Count > 0) { args.Handled = true; int index = _model.ActiveTab is null ? 0 : _model.Tabs.IndexOf(_model.ActiveTab); _model.ActiveTab = _model.Tabs[(index + ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1) + _model.Tabs.Count) % _model.Tabs.Count]; }
    }
}
