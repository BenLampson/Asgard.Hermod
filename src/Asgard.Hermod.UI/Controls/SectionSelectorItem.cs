namespace Asgard.Hermod.UI.Controls;

/// <summary>公共下划线选择器的容器，在格式列表更换或移除时保留本地对齐值。</summary>
public class SectionSelectorItem : ListBoxItem
{
    static SectionSelectorItem() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SectionSelectorItem), new FrameworkPropertyMetadata(typeof(SectionSelectorItem)));
    public SectionSelectorItem() { HorizontalContentAlignment = HorizontalAlignment.Center; VerticalContentAlignment = VerticalAlignment.Center; }
}
