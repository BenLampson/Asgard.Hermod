namespace Asgard.Hermod.UI.Controls;

/// <summary>协议无关的内部页签条；业务提供标题、提示及关闭动作。</summary>
public class WorkspaceTabStrip : ListBox
{
    /// <summary>模块提供相同的受保护关闭命令；中键参数是指针所在项。</summary>
    public static readonly DependencyProperty CloseCommandProperty = DependencyProperty.Register(nameof(CloseCommand), typeof(ICommand), typeof(WorkspaceTabStrip));
    /// <summary>未配置时不提供中键关闭。</summary>
    public ICommand? CloseCommand { get => (ICommand?)GetValue(CloseCommandProperty); set => SetValue(CloseCommandProperty, value); }
    /// <summary>业务提供完整名称路径，标题截断不影响提示与可访问名称。</summary>
    public static readonly DependencyProperty ToolTipMemberPathProperty = DependencyProperty.Register(nameof(ToolTipMemberPath), typeof(string), typeof(WorkspaceTabStrip), new PropertyMetadata(""));
    private static readonly DependencyPropertyKey HeaderWidthPropertyKey = DependencyProperty.RegisterReadOnly(nameof(HeaderWidth), typeof(double),
        typeof(WorkspaceTabStrip), new PropertyMetadata(180d));
    /// <summary>标题宽度随页签条变化；超过三个页签继续使用原有滚动与溢出入口。</summary>
    public static readonly DependencyProperty HeaderWidthProperty = HeaderWidthPropertyKey.DependencyProperty;
    /// <summary>为图标和关闭按钮预留宽度后的文字空间。</summary>
    public double HeaderWidth => (double)GetValue(HeaderWidthProperty);
    /// <summary>绑定完整标题的成员路径。</summary>
    public string ToolTipMemberPath { get => (string)GetValue(ToolTipMemberPathProperty); set => SetValue(ToolTipMemberPathProperty, value); }
    /// <summary>复用 ListBox 单选与键盘导航，横向滚动由公共模板提供。</summary>
    static WorkspaceTabStrip() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceTabStrip),
        new FrameworkPropertyMetadata(typeof(WorkspaceTabStrip)));
    /// <summary>更新标题宽度；提示生命周期交给 WPF，键盘完整名称由自动化属性提供。</summary>
    public WorkspaceTabStrip()
    {
        SizeChanged += (_, _) => UpdateHeaderWidth();
    }
    /// <summary>在内部视口处理普通滚轮前，将 Shift 滚轮转换为水平像素滚动。</summary>
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs args)
    {
        base.OnPreviewMouseWheel(args);
        HandleMouseWheel(args, Keyboard.Modifiers);
    }
    /// <summary>只滚动本控件的模板视口，不改变活动页签或键盘焦点。</summary>
    internal void HandleMouseWheel(MouseWheelEventArgs args, ModifierKeys modifiers)
    {
        if (args.Handled || modifiers != ModifierKeys.Shift || args.Delta == 0
            || Template?.FindName("PART_ScrollViewer", this) is not ScrollViewer viewer
            || viewer.ScrollableWidth <= 0) return;
        double next = Math.Clamp(viewer.HorizontalOffset - args.Delta / 120d * 48, 0, viewer.ScrollableWidth);
        viewer.ScrollToHorizontalOffset(next);
        args.Handled = true;
    }
    /// <summary>关闭命中页签而非活动项，仍经过业务的草稿、事务和运行中查询预检。</summary>
    protected override void OnPreviewMouseDown(MouseButtonEventArgs args)
    {
        base.OnPreviewMouseDown(args);
        if (args.Handled || args.ChangedButton != MouseButton.Middle || CloseCommand is not { } close) return;
        for (DependencyObject? source = args.OriginalSource as DependencyObject; source is not null; source = source is FrameworkContentElement content ? content.Parent : VisualTreeHelper.GetParent(source))
        {
            if (source is not ListBoxItem item) continue;
            if (!ReferenceEquals(ItemsControlFromItemContainer(item), this)) return;
            object value = ItemContainerGenerator.ItemFromContainer(item);
            if (close.CanExecute(value)) close.Execute(value);
            args.Handled = true; return;
        }
    }
    /// <summary>增删页签只调整展示宽度，不影响业务对象和草稿。</summary>
    protected override void OnItemsChanged(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        base.OnItemsChanged(e);
        UpdateHeaderWidth();
    }
    private void UpdateHeaderWidth() => SetValue(HeaderWidthPropertyKey,
        Math.Clamp(ActualWidth / Math.Clamp(Items.Count, 1, 3) - 72, 100, 240));
    /// <summary>公共控件不识别协议字段，绑定路径由模块指定。</summary>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (ToolTipMemberPath.Length == 0 || element is not ListBoxItem container) return;
        container.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(ToolTipMemberPath));
        container.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding(ToolTipMemberPath));
    }
}
