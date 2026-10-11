namespace Asgard.Hermod.UI.Controls;

/// <summary>可复用的单选卡片，类型、服务商和保存位置共享视觉与键盘行为。</summary>
public class SelectionCard : RadioButton
{
    /// <summary>主要选项名称。</summary>
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(
        nameof(Heading), typeof(string), typeof(SelectionCard), new PropertyMetadata(""));
    /// <summary>选项说明。</summary>
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(SelectionCard), new PropertyMetadata(""));
    /// <summary>选项图标名称。</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(SelectionCard), new PropertyMetadata("database"));

    /// <summary>将卡片模板纳入控件默认主题。</summary>
    static SelectionCard() => DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectionCard),
        new FrameworkPropertyMetadata(typeof(SelectionCard)));

    /// <summary>卡片主标题。</summary>
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    /// <summary>卡片次要说明。</summary>
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    /// <summary>使用已有图标目录的矢量图标。</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
