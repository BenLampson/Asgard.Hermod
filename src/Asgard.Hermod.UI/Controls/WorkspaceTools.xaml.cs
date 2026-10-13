using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Asgard.Hermod.UI.Controls;

/// <summary>类型无关的工具入口，负责筛选、可滚动浮层与焦点恢复；动作由工作区提供。</summary>
public partial class WorkspaceTools : UserControl
{
    /// <summary>注册工具列表；支持可观察集合的动态扩展。</summary>
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable<WorkspaceTool>), typeof(WorkspaceTools), new PropertyMetadata(null, OnItemsChanged));
    /// <summary>浮层显隐可由宿主关闭。</summary>
    public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
        nameof(IsOpen), typeof(bool), typeof(WorkspaceTools), new PropertyMetadata(false));
    /// <summary>浮层关闭后恢复焦点的正文控件。</summary>
    public static readonly DependencyProperty FocusTargetProperty = DependencyProperty.Register(
        nameof(FocusTarget), typeof(UIElement), typeof(WorkspaceTools));
    private INotifyCollectionChanged? _observed;
    private bool _restoreFocus = true;

    /// <summary>与模板共享过滤结果，集合变化不会重建面板。</summary>
    public ObservableCollection<WorkspaceTool> FilteredTools { get; } = [];
    /// <summary>模块仅提供工具数据，不复制菜单布局。</summary>
    public IEnumerable<WorkspaceTool>? Items { get => (IEnumerable<WorkspaceTool>?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    /// <summary>浮层是否打开。</summary>
    public bool IsOpen { get => (bool)GetValue(IsOpenProperty); set => SetValue(IsOpenProperty, value); }
    /// <summary>关闭工具后恢复正文输入。</summary>
    public UIElement? FocusTarget { get => (UIElement?)GetValue(FocusTargetProperty); set => SetValue(FocusTargetProperty, value); }

    /// <summary>离开页签关闭浮层并解除集合订阅，不把焦点抢回不可见正文。</summary>
    public WorkspaceTools()
    {
        InitializeComponent();
        Loaded += (_, _) => { ObserveItems(); RefreshTools(); };
        Unloaded += (_, _) => { Close(false); UnobserveItems(); };
    }

    /// <summary>Escape 与关闭按钮保持相同焦点语义。</summary>
    public void Close(bool restoreFocus = true) { _restoreFocus = restoreFocus; IsOpen = false; }
    private static void OnItemsChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var tools = (WorkspaceTools)owner;
        tools.UnobserveItems(); if (tools.IsLoaded) tools.ObserveItems(); tools.RefreshTools();
    }
    private void ObserveItems() { UnobserveItems(); _observed = Items as INotifyCollectionChanged; if (_observed is not null) _observed.CollectionChanged += OnCollectionChanged; }
    private void UnobserveItems() { if (_observed is not null) _observed.CollectionChanged -= OnCollectionChanged; _observed = null; }
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args) => RefreshTools();
    private void OnFilterChanged(object sender, TextChangedEventArgs args) => RefreshTools();
    /// <summary>筛选仅匹配显示信息，行为始终由稳定标识和命令决定。</summary>
    private void RefreshTools()
    {
        if (ToolFilter is null) return;
        string query = ToolFilter.Text.Trim(); FilteredTools.Clear();
        foreach (var tool in Items ?? [])
            if (query.Length == 0 || tool.Title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                || tool.Group.Contains(query, StringComparison.CurrentCultureIgnoreCase)) FilteredTools.Add(tool);
        EmptyTools.Visibility = FilteredTools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnOpened(object? sender, EventArgs args)
    {
        _restoreFocus = true;
        ToolsPanel.Width = Math.Min(340, Math.Max(160, (Window.GetWindow(this)?.ActualWidth ?? 372) - 32));
        ToolsPopup.HorizontalOffset = ToolsTrigger.ActualWidth - ToolsPanel.Width;
        ToolFilter.Clear(); ToolFilter.Focus();
    }
    /// <summary>选取搜索等动作时由命令拥有新焦点，其余关闭恢复正文。</summary>
    private void OnToolClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: WorkspaceTool tool }) Close(tool.RestoreFocus);
    }
    private void OnClosed(object? sender, EventArgs args) { if (_restoreFocus && IsLoaded) FocusTarget?.Focus(); }
    private void OnCloseClick(object sender, RoutedEventArgs args) => Close();
    private void OnToggleClick(object sender, RoutedEventArgs args) => IsOpen = !IsOpen;
    private void OnPopupKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape) return; Close(); args.Handled = true;
    }
}
