namespace Asgard.Hermod.UI.Controls;

/// <summary>内容分区和格式选择使用 Pencil 下划线页签，复用原生单选和键盘导航。</summary>
public class SectionSelector : ListBox
{
    /// <summary>与表单分段控件分别加载正确的公共视觉模板。</summary>
    static SectionSelector() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SectionSelector), new FrameworkPropertyMetadata(typeof(SectionSelector)));
}
