using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Asgard.Hermod.MySql.UI.ViewModels;

namespace Asgard.Hermod.MySql.UI.Views;

/// <summary>只协调 WPF 视图缓存、树选择和窄窗口抽屉，不包含协议逻辑。</summary>
public partial class MySqlWorkspaceView : UserControl
{
    private readonly Dictionary<MySqlTabViewModel, MySqlTabView> _views = [];
    private readonly Dictionary<MySqlTabViewModel, MySqlCategoryView> _overviewViews = [];
    private MySqlWorkspaceViewModel? _model;
    private bool _compact, _drawer;
    private bool _treeVisible = true;
    private double _treeWidth = 232;
    /// <summary>创建模块状态和依赖；真实连接或写入由显式操作启动。</summary>
    public MySqlWorkspaceView()
    {
        InitializeComponent(); DataContextChanged += (_, _) => Attach(); Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = null; };
    }
    private void Attach() { if (_model is not null) _model.PropertyChanged -= OnModelChanged; _model = DataContext as MySqlWorkspaceViewModel; if (_model is not null) _model.PropertyChanged += OnModelChanged; ShowTab(); }
    private void OnModelChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName is nameof(MySqlWorkspaceViewModel.ActiveTab) or nameof(MySqlWorkspaceViewModel.ActiveSession)) ShowTab(); }
    private void ShowTab()
    {
        // 分屏收起时保留同一助手视图与会话，只更新当前 Session 的绑定。
        if (_model is null) return;
        foreach (var stale in _views.Keys.Where(tab => !_model.Tabs.Contains(tab)).ToArray()) _views.Remove(stale);
        foreach (var stale in _overviewViews.Keys.Where(tab => !_model.Tabs.Contains(tab)).ToArray()) _overviewViews.Remove(stale);
        if (_model.ActiveTab is not { } tab) { TabHost.Content = null; return; }
        if (tab is { IsOverview: true, SessionOwner: { } session })
        {
            if (!_overviewViews.TryGetValue(tab, out var overview))
            { overview = new(session, selected => _model.OpenAsync(selected), _model.RefreshOverviewCommand); _overviewViews.Add(tab, overview); }
            TabHost.Content = overview; ObjectTabs.ScrollIntoView(tab); return;
        }
        if (!_views.TryGetValue(tab, out var view)) { view = new() { DataContext = tab }; _views.Add(tab, view); }
        TabHost.Content = view; ObjectTabs.ScrollIntoView(tab);
    }
    private void OnSelectedObject(object sender, RoutedPropertyChangedEventArgs<object> args)
    {
        if (_model is null || args.NewValue is not MySqlTreeNode node) return;
        if (IsCategory(node))
        {
            if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return;
            _model.SelectedNode = node; _model.ShowOverview(node); return;
        }
        _model.SelectedNode = node;
    }
    /// <summary>只响应真实对象行的双击，避免原生树同时展开对象的元数据。</summary>
    private async void OnObjectDoubleClick(object sender, MouseButtonEventArgs args)
    {
        if (_model is null || _model.Connection.IsOpen || args.ChangedButton != MouseButton.Left || args.ClickCount != 2) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
        {
            if (source is ButtonBase) return;
            if (source is not TreeViewItem item) continue;
            if (item.DataContext is not MySqlTreeNode node || node.IsPlaceholder || node.Object.Kind == MySqlObjectKind.Database || IsCategory(node)) return;
            args.Handled = true;
            await OpenNodeAsync(node);
            return;
        }
    }
    private async void OnTreeKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || _model is null || _model.Connection.IsOpen || _model.SelectedNode is not { } node) return;
        for (var source = args.OriginalSource as DependencyObject; source is not null && source != ObjectTree; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source))
            if (source is ButtonBase) return;
        args.Handled = true;
        await OpenNodeAsync(node);
    }
    private async Task OpenNodeAsync(MySqlTreeNode node)
    {
        if (_model is null || _model.Connection.IsOpen) return;
        if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return;
        await _model.OpenAsync(node);
        ShowTab();
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
            if (item.DataContext is MySqlTreeNode category && IsCategory(category) && ReferenceEquals(_model.SelectedNode, category))
            {
                if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return;
                _model.ShowOverview(category); return;
            }
            if (item.DataContext is MySqlTreeNode { Object.Kind: MySqlObjectKind.Database } node && ReferenceEquals(_model.SelectedNode, node))
            { if (_model.IsCatalog) _model.OpenDatabase(node.Object.Name); else node.IsExpanded = !node.IsExpanded; args.Handled = true; }
            return;
        }
    }
    private static bool IsCategory(MySqlTreeNode node) => node.Object.Kind is MySqlObjectKind.Tables or MySqlObjectKind.Views
        or MySqlObjectKind.Procedures or MySqlObjectKind.Functions or MySqlObjectKind.Events;
    private void OnTabChanged(object sender, SelectionChangedEventArgs args) => ShowTab();
    private void OnBodySizeChanged(object sender, SizeChangedEventArgs args)
    {
        ObjectTree.MaxHeight = Math.Max(80, args.NewSize.Height - 160);
        bool compact = args.NewSize.Width < 1040; if (!_compact && compact && _treeVisible) _treeWidth = TreeColumn.ActualWidth;
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
        if (args.Key == Key.Escape && _drawer) { _drawer = false; Layout(); args.Handled = true; return; }
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (args.Key == Key.W) { if (TabHost.Content is MySqlTabView current && !current.CommitInput()) return; args.Handled = true; await _model.CloseTabCommand.ExecuteAsync(_model.ActiveTab); }
        if (args.Key == Key.Tab && _model.ActiveDocuments.Count > 0) { args.Handled = true; var documents = _model.ActiveDocuments; int index = _model.ActiveTab is null ? -1 : documents.ToList().IndexOf(_model.ActiveTab); _model.ActiveTab = documents[(index + ((Keyboard.Modifiers & ModifierKeys.Shift) == 0 ? 1 : -1) + documents.Count) % documents.Count]; }
    }

    /// <summary>固定来源 Session，其他窗口和活动页签不会重定向已有助手。</summary>
    private void OnOpenAgent(object sender, RoutedEventArgs args)
    {
        if (_model is not { ActiveSession: { } session, Assistant: { } assistant } model || TryFindResource(typeof(IAgentWindowService)) is not IAgentWindowService windows) return;
        var origin = Window.GetWindow(this);
        windows.Open(new(session, assistant, Text.Format("ChatDatabaseSource", model.Name, "MySQL", session.Header), model.Endpoint, "brand-mysql",
            () => new MySqlAssistantView(), () =>
            {
                if (assistant.IsSourceClosed || !model.Sessions.Contains(session)) return;
                model.ActiveSession = session;
                if (origin is not null) { if (origin.WindowState == WindowState.Minimized) origin.WindowState = WindowState.Normal; origin.Show(); origin.Activate(); }
            }, () => assistant.IsSourceClosed));
    }
}
