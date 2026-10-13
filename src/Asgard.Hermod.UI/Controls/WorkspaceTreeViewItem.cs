namespace Asgard.Hermod.UI.Controls;

/// <summary>本地对齐值覆盖 WPF 默认祖先绑定，回收节点不会产生绑定错误。</summary>
public class WorkspaceTreeViewItem : TreeViewItem
{
    /// <summary>正文通过 ItemContainerStyle 提供模板，不加载含祖先绑定的系统主题模板。</summary>
    static WorkspaceTreeViewItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceTreeViewItem),
        new FrameworkPropertyMetadata(typeof(WorkspaceTreeViewItem)));
    /// <summary>节点移除样式或离开可视树时仍保留合法对齐。</summary>
    public WorkspaceTreeViewItem() { HorizontalContentAlignment = HorizontalAlignment.Stretch; VerticalContentAlignment = VerticalAlignment.Center; }
    /// <summary>子节点使用相同的容器生命周期规则。</summary>
    protected override DependencyObject GetContainerForItemOverride() => new WorkspaceTreeViewItem();
    /// <summary>展开由正文的单击与原生方向键控制，双击不会重复展开再收起。</summary>
    protected override void OnMouseDoubleClick(MouseButtonEventArgs args) => args.Handled = true;
}
