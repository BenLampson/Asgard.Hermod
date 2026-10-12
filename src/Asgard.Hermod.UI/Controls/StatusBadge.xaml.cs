namespace Asgard.Hermod.UI.Controls;

/// <summary>使用主题语义色显示服务或会话状态的紧凑徽标。</summary>
public partial class StatusBadge : UserControl
{
    /// <summary>声明状态说明的依赖属性。</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusBadge), new PropertyMetadata(string.Empty));

    /// <summary>声明状态色调的依赖属性。</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Models.StatusTone), typeof(StatusBadge), new PropertyMetadata(Models.StatusTone.Muted));

    /// <summary>行内状态仅显示语义圆点和辅助文字，适用于内容页头。</summary>
    public static readonly DependencyProperty IsInlineProperty = DependencyProperty.Register(
        nameof(IsInline), typeof(bool), typeof(StatusBadge), new PropertyMetadata(false));

    /// <summary>使用无背景的行内状态样式。</summary>
    public bool IsInline { get => (bool)GetValue(IsInlineProperty); set => SetValue(IsInlineProperty, value); }

    /// <summary>获取或设置面向用户的状态说明。</summary>
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>获取或设置语义色；主题切换时同步更新底色和文字。</summary>
    public Models.StatusTone Tone
    {
        get => (Models.StatusTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>初始化设计中的圆点与文字布局。</summary>
    public StatusBadge() => InitializeComponent();
}
