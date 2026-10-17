namespace Asgard.Hermod.UI.Controls;

/// <summary>切换 Session 回收文档页签时，保留本地对齐值以避免系统样式寻找已移除的祖先。</summary>
public sealed class WorkspaceTabStripItem : ListBoxItem
{
    /// <summary>模板由共享样式提供；回收时不加载包含祖先绑定的系统主题模板。</summary>
    static WorkspaceTabStripItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceTabStripItem), new FrameworkPropertyMetadata(typeof(WorkspaceTabStripItem)));
    /// <summary>对齐值与共享页签模板一致，并在脱离可视树后仍然有效。</summary>
    public WorkspaceTabStripItem()
    {
        HorizontalContentAlignment = HorizontalAlignment.Center;
        VerticalContentAlignment = VerticalAlignment.Center;
    }
}
