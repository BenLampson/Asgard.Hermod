namespace Asgard.Hermod.UI.Controls;

/// <summary>协议无关的内部页签条；业务提供标题、提示及关闭动作。</summary>
public class WorkspaceTabStrip : ListBox
{
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
