namespace Asgard.Hermod.UI.Controls;

/// <summary>引用 Pencil ui/segmented 的公共单选控件，值、文案与选项由调用者提供。</summary>
public class SegmentedSelector : ListBox
{
    /// <summary>通过共享模板提供等宽选项、主题状态及 ListBox 原生键盘导航。</summary>
    static SegmentedSelector() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SegmentedSelector),
        new FrameworkPropertyMetadata(typeof(SegmentedSelector)));
}
