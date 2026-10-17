namespace Asgard.Hermod.UI.Controls;

/// <summary>懒加载和虚拟化树使用稳定的容器对齐，不在卸载时查找已消失的祖先。</summary>
public class WorkspaceTreeView : TreeView
{
    /// <summary>显示树和懒加载同时使用，回收已经离开视口的容器。</summary>
    public WorkspaceTreeView()
    {
        VirtualizingPanel.SetIsVirtualizing(this, true); VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(this, true);
    }
    /// <summary>所有层级都使用同一公共容器。</summary>
    protected override DependencyObject GetContainerForItemOverride() => new WorkspaceTreeViewItem();
}
