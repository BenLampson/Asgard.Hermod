namespace Asgard.Hermod.UI.Controls;

/// <summary>协议无关的内部页签条；业务提供标题、提示及关闭动作。</summary>
public class WorkspaceTabStrip : ListBox
{
    /// <summary>业务提供完整名称路径，标题截断不影响提示与可访问名称。</summary>
    public static readonly DependencyProperty ToolTipMemberPathProperty = DependencyProperty.Register(nameof(ToolTipMemberPath), typeof(string), typeof(WorkspaceTabStrip), new PropertyMetadata(""));
    private ToolTip? _focusHint;
    /// <summary>绑定完整标题的成员路径。</summary>
    public string ToolTipMemberPath { get => (string)GetValue(ToolTipMemberPathProperty); set => SetValue(ToolTipMemberPathProperty, value); }
    /// <summary>复用 ListBox 单选与键盘导航，横向滚动由公共模板提供。</summary>
    static WorkspaceTabStrip() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceTabStrip),
        new FrameworkPropertyMetadata(typeof(WorkspaceTabStrip)));
    /// <summary>键盘焦点同时提供完整标题提示，关闭或失焦时及时移除。</summary>
    public WorkspaceTabStrip()
    {
        GotKeyboardFocus += (_, args) =>
        {
            if (_focusHint is not null) _focusHint.IsOpen = false;
            if (ItemsControl.ContainerFromElement(this, args.NewFocus as DependencyObject) is not ListBoxItem item || item.ToolTip is null) return;
            _focusHint = new ToolTip { Content = item.ToolTip, PlacementTarget = item, IsOpen = true };
        };
        LostKeyboardFocus += (_, _) => { if (_focusHint is not null) _focusHint.IsOpen = false; };
        Unloaded += (_, _) => { if (_focusHint is not null) _focusHint.IsOpen = false; };
    }
    /// <summary>公共控件不识别协议字段，绑定路径由模块指定。</summary>
    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (ToolTipMemberPath.Length == 0 || element is not ListBoxItem container) return;
        container.SetBinding(ToolTipProperty, new System.Windows.Data.Binding(ToolTipMemberPath));
        container.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding(ToolTipMemberPath));
    }
}
