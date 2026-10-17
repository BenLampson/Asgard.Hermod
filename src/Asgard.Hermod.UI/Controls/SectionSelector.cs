namespace Asgard.Hermod.UI.Controls;

/// <summary>内容分区和格式选择使用 Pencil 下划线页签，复用原生单选和键盘导航。</summary>
public class SectionSelector : ListBox
{
    /// <summary>与表单分段控件分别加载正确的公共视觉模板。</summary>
    static SectionSelector() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SectionSelector), new FrameworkPropertyMetadata(typeof(SectionSelector)));
    /// <summary>类型切换回收格式选项时，不加载依赖已移除祖先的系统 ListBoxItem 样式。</summary>
    protected override DependencyObject GetContainerForItemOverride() => new SectionSelectorItem();
}
