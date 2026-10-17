namespace Asgard.Hermod.UI.Controls;

/// <summary>本地对齐值覆盖 WPF 默认祖先绑定，回收节点不会产生绑定错误。</summary>
public class WorkspaceTreeViewItem : TreeViewItem
{
    /// <summary>正文通过 ItemContainerStyle 提供模板，不加载含祖先绑定的系统主题模板。</summary>
    static WorkspaceTreeViewItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceTreeViewItem), new FrameworkPropertyMetadata(typeof(WorkspaceTreeViewItem)));
        // 派生类处理器先于 WPF 基类执行，避免基类提前展开祖先或安排整棵子树的滚动。
        EventManager.RegisterClassHandler(typeof(WorkspaceTreeViewItem), RequestBringIntoViewEvent, new RequestBringIntoViewEventHandler(OnBringRowIntoView));
    }
    /// <summary>节点移除样式或离开可视树时仍保留合法对齐。</summary>
    public WorkspaceTreeViewItem()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Center;
    }
    private static void OnBringRowIntoView(object sender, RequestBringIntoViewEventArgs args)
    {
        // 默认请求包含整棵已展开子树，会把视口跳回父节点；只让当前实际行进入视口。
        if (sender is not WorkspaceTreeViewItem item || !ReferenceEquals(args.TargetObject, item)) return;
        item.ApplyTemplate();
        if (item.Template?.FindName("Row", item) is not FrameworkElement row) return;
        args.Handled = true; row.BringIntoView();
    }
    /// <summary>子节点使用相同的容器生命周期规则。</summary>
    protected override DependencyObject GetContainerForItemOverride() => new WorkspaceTreeViewItem();
    /// <summary>展开由正文的单击与原生方向键控制，双击不会重复展开再收起。</summary>
    protected override void OnMouseDoubleClick(MouseButtonEventArgs args) => args.Handled = true;
}
