namespace Asgard.Hermod.UI.Controls;

/// <summary>
/// 根据所属窗口的 ResizeMode 显示按钮，通过窗口命令完成最小化、最大化或还原、关闭。
/// </summary>
public partial class WindowControls : UserControl
{
    private static readonly DependencyPropertyKey OwnerPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Owner), typeof(Window), typeof(WindowControls), new PropertyMetadata(null));
    /// <summary>实际挂载的窗口；纯控件离屏校验保持为空，不创建原生窗口。</summary>
    public static readonly DependencyProperty OwnerProperty = OwnerPropertyKey.DependencyProperty;
    /// <summary>标题提示与最大化状态跟随实际所属窗口。</summary>
    public Window? Owner => (Window?)GetValue(OwnerProperty);
    /// <summary>工作窗口标题使用三枚三十六乘四十四 DIP 的独立命中区域。</summary>
    public static readonly DependencyProperty IsWorkspaceProperty = DependencyProperty.Register(
        nameof(IsWorkspace), typeof(bool), typeof(WindowControls), new FrameworkPropertyMetadata(false));
    /// <summary>是否遵循工作窗口标题规格。</summary>
    public bool IsWorkspace { get => (bool)GetValue(IsWorkspaceProperty); set => SetValue(IsWorkspaceProperty, value); }
    /// <summary>紧凑标题栏使用十四像素图标按钮和八像素间距，主窗保留原有尺寸。</summary>
    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.Register(
        nameof(IsCompact), typeof(bool), typeof(WindowControls), new FrameworkPropertyMetadata(false));

    /// <summary>
    /// 创建窗口按钮。
    /// </summary>
    public WindowControls()
    {
        InitializeComponent();
        Loaded += (_, _) => SetValue(OwnerPropertyKey, Window.GetWindow(this));
        Unloaded += (_, _) => SetValue(OwnerPropertyKey, null);
    }

    /// <summary>选择子窗标题栏的紧凑按钮布局。</summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
    }
    private void OnMinimize(object sender, RoutedEventArgs args) => Dispatch(SystemCommands.MinimizeWindowCommand);
    private void OnMaximize(object sender, RoutedEventArgs args) => Dispatch(Owner?.WindowState == WindowState.Maximized ? SystemCommands.RestoreWindowCommand : SystemCommands.MaximizeWindowCommand);
    private void OnClose(object sender, RoutedEventArgs args) => Dispatch(SystemCommands.CloseWindowCommand);
    private void Dispatch(RoutedCommand command)
    {
        // 保留 HermodWindow 的原有命令和 Closing 确认边界；没有所属窗口时点击无副作用。
        if (Window.GetWindow(this) is { } window && command.CanExecute(null, window)) command.Execute(null, window);
    }
}
