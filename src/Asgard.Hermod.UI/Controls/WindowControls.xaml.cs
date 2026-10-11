namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 根据所属窗口的 ResizeMode 显示按钮，通过窗口命令完成最小化、最大化或还原、关闭。
/// </summary>
public partial class WindowControls : UserControl
{
    /// <summary>紧凑标题栏使用十四像素图标按钮和八像素间距，主窗保留原有尺寸。</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(WindowControls), new FrameworkPropertyMetadata(false));

    /// <summary>
    /// 创建窗口按钮。
    /// </summary>
    public WindowControls()
    {
        InitializeComponent();
    }

    /// <summary>选择子窗标题栏的紧凑按钮布局。</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
}
