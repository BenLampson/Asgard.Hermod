using System.Windows.Controls.Primitives;

namespace Asgard.Hermod.UI.Controls;

/// <summary>保留两侧内容实例的可收起分屏，隐藏时不释放对话、草稿或连接。</summary>
public sealed class WorkspaceSplitView : Grid
{
    /// <summary>主工作内容。</summary>
    public static readonly DependencyProperty MainContentProperty = DependencyProperty.Register(nameof(MainContent), typeof(object), typeof(WorkspaceSplitView), new PropertyMetadata(null, Changed));
    /// <summary>辅助分屏内容。</summary>
    public static readonly DependencyProperty PaneContentProperty = DependencyProperty.Register(nameof(PaneContent), typeof(object), typeof(WorkspaceSplitView), new PropertyMetadata(null, Changed));
    /// <summary>由调用方显式打开；尺寸变化不会自动打开。</summary>
    public static readonly DependencyProperty IsPaneOpenProperty = DependencyProperty.Register(nameof(IsPaneOpen), typeof(bool), typeof(WorkspaceSplitView), new PropertyMetadata(false, Changed));
    /// <summary>本窗口记忆的辅助宽度；收起时保留。</summary>
    public static readonly DependencyProperty PaneWidthProperty = DependencyProperty.Register(nameof(PaneWidth), typeof(double), typeof(WorkspaceSplitView), new PropertyMetadata(420d, Changed));
    // ContentControl 持有逻辑子节点，确保分屏内的 DataContext 与名称域正常继承。
    private readonly ContentControl _main = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Focusable = false };
    private readonly ContentControl _pane = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Focusable = false };
    private readonly GridSplitter _splitter = new() { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, ResizeDirection = GridResizeDirection.Columns, ResizeBehavior = GridResizeBehavior.PreviousAndNext, KeyboardIncrement = 16, ShowsPreview = true };
    private bool _updating;

    /// <summary>只提供布局与原生键盘分隔操作，不认识任何模块或 AI 状态。</summary>
    public WorkspaceSplitView()
    {
        ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = new(0) }); ColumnDefinitions.Add(new() { Width = new(0) });
        SetColumn(_splitter, 1); SetColumn(_pane, 2);
        _splitter.SetResourceReference(Control.BackgroundProperty, "LineSoftBrush");
        System.Windows.Automation.AutomationProperties.SetName(_splitter, Text.Get("WorkspacePaneResize"));
        Children.Add(_main); Children.Add(_splitter); Children.Add(_pane);
        _splitter.DragCompleted += (_, _) => { if (IsPaneOpen) SetCurrentValue(PaneWidthProperty, ColumnDefinitions[2].ActualWidth); };
        DependencyPropertyDescriptor.FromProperty(ColumnDefinition.WidthProperty, typeof(ColumnDefinition))?.AddValueChanged(ColumnDefinitions[2], (_, _) =>
        {
            if (!_updating && IsPaneOpen && ColumnDefinitions[2].Width.IsAbsolute)
                SetCurrentValue(PaneWidthProperty, ColumnDefinitions[2].Width.Value);
        });
        SizeChanged += (_, _) => Refresh(); Refresh();
    }

    /// <summary>主内容插槽。</summary>
    public object? MainContent { get => GetValue(MainContentProperty); set => SetValue(MainContentProperty, value); }
    /// <summary>辅助内容插槽。</summary>
    public object? PaneContent { get => GetValue(PaneContentProperty); set => SetValue(PaneContentProperty, value); }
    /// <summary>辅助分屏显隐。</summary>
    public bool IsPaneOpen { get => (bool)GetValue(IsPaneOpenProperty); set => SetValue(IsPaneOpenProperty, value); }
    /// <summary>辅助分屏上次拖动宽度。</summary>
    public double PaneWidth { get => (double)GetValue(PaneWidthProperty); set => SetValue(PaneWidthProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((WorkspaceSplitView)sender).Refresh();
    private void Refresh()
    {
        _updating = true;
        try
        {
            _main.Content = MainContent; _pane.Content = PaneContent;
            _pane.Visibility = _splitter.Visibility = IsPaneOpen ? Visibility.Visible : Visibility.Collapsed;
            ColumnDefinitions[0].MinWidth = IsPaneOpen ? Math.Min(360, ActualWidth / 2) : 0;
            ColumnDefinitions[1].Width = new(IsPaneOpen ? 6 : 0);
            ColumnDefinitions[2].MinWidth = IsPaneOpen ? Math.Min(280, ActualWidth / 2) : 0;
            ColumnDefinitions[2].MaxWidth = IsPaneOpen ? Math.Max(0, ActualWidth - ColumnDefinitions[0].MinWidth - 6) : double.PositiveInfinity;
            double requested = double.IsFinite(PaneWidth) ? PaneWidth : 420;
            ColumnDefinitions[2].Width = new(IsPaneOpen ? Math.Clamp(requested, 0, ColumnDefinitions[2].MaxWidth) : 0);
        }
        finally { _updating = false; }
    }
}
