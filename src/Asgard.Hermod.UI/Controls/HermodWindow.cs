using System.Windows.Interop;
using System.Windows.Shell;
using Asgard.Hermod.UI.Windowing;

namespace Asgard.Hermod.UI.Controls;

/// <summary>统一承载 Hermod 的窗口边框、标题区域和原生窗口行为。</summary>
[TemplatePart(Name = "PART_WindowFrame", Type = typeof(Border))]
[TemplatePart(Name = "PART_Caption", Type = typeof(ContentPresenter))]
[TemplatePart(Name = "PART_Footer", Type = typeof(ContentPresenter))]
public class HermodWindow : Window
{
    /// <summary>标题区域的内容；为空时使用展示窗口标题的标准标题栏。</summary>
    public static readonly DependencyProperty CaptionContentProperty = DependencyProperty.Register(
        nameof(CaptionContent), typeof(object), typeof(HermodWindow), new FrameworkPropertyMetadata(null));

    /// <summary>标题区域高度，同时决定模板布局与原生拖拽区域。</summary>
    public static readonly DependencyProperty CaptionHeightProperty = DependencyProperty.Register(
        nameof(CaptionHeight), typeof(double), typeof(HermodWindow),
        new FrameworkPropertyMetadata(42d, OnCaptionHeightChanged), IsValidCaptionHeight);

    /// <summary>正文下方的页脚内容；为空时整个页脚槽折叠。</summary>
    public static readonly DependencyProperty FooterContentProperty = DependencyProperty.Register(
        nameof(FooterContent), typeof(object), typeof(HermodWindow), new FrameworkPropertyMetadata(null));

    // 每个窗口持有自己的 Chrome，避免共享实例使多个窗口互相修改边界。
    private readonly WindowChrome _chrome = new()
    {
        CaptionHeight = 42,
        ResizeBorderThickness = new Thickness(6),
        // 非零合成边缘让 WindowChrome 清除 HRGN，由 DWM 统一绘制平滑窗口轮廓。
        GlassFrameThickness = new Thickness(1),
        CornerRadius = new CornerRadius(0),
        UseAeroCaptionButtons = false
    };
    private HwndSource? _windowSource;

    /// <summary>派生窗口自动使用 UI 程序集的窗口模板，并响应调整大小模式的变化。</summary>
    static HermodWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(HermodWindow),
            new FrameworkPropertyMetadata(typeof(HermodWindow)));
        TitleProperty.OverrideMetadata(typeof(HermodWindow), new FrameworkPropertyMetadata(Text.Get("Hermod")));
        ResizeModeProperty.OverrideMetadata(typeof(HermodWindow),
            new FrameworkPropertyMetadata(ResizeMode.CanResize, OnResizeModeChanged));
    }

    /// <summary>使用不透明的原生窗口和自定义标题栏，系统命令仍由 WPF 路由。</summary>
    public HermodWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        WindowChrome.SetWindowChrome(this, _chrome);
        CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, OnMinimize, OnCanMinimize));
        CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, OnMaximize, OnCanMaximize));
        CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, OnRestore, OnCanRestore));
        CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, OnClose));
        StateChanged += OnWindowStateChanged;
        UpdateWindowChrome();
    }

    /// <summary>允许主窗口提供搜索和账号等定制标题栏，正文继续使用 Content。</summary>
    public object? CaptionContent
    {
        get => GetValue(CaptionContentProperty);
        set => SetValue(CaptionContentProperty, value);
    }

    /// <summary>主窗口默认四十二个逻辑像素，紧凑弹窗可使用三十六个逻辑像素。</summary>
    public double CaptionHeight
    {
        get => (double)GetValue(CaptionHeightProperty);
        set => SetValue(CaptionHeightProperty, value);
    }

    /// <summary>业务提供独立操作栏等页脚内容，不与正文及标题重复组合。</summary>
    public object? FooterContent
    {
        get => GetValue(FooterContentProperty);
        set => SetValue(FooterContentProperty, value);
    }

    /// <summary>接入当前显示器的工作区计算，不覆盖 WPF 对最小窗口尺寸的处理。</summary>
    protected override void OnSourceInitialized(EventArgs args)
    {
        base.OnSourceInitialized(args);
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(OnWindowMessage);
        UpdateNativeWindowFrame();
    }

    /// <summary>动态主题资源变化后同步已创建 HWND 的颜色，无需依赖业务 ViewModel。</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (args.Property == BorderBrushProperty || args.Property == BackgroundProperty)
            UpdateNativeWindowFrame();
    }

    /// <summary>原生轮廓使用当前窗口主题；所有派生主窗口和弹窗共用此规则。</summary>
    private void UpdateNativeWindowFrame()
    {
        if (_windowSource is not { IsDisposed: false } || BorderBrush is not SolidColorBrush border)
            return;
        bool dark = Background is SolidColorBrush background
            && background.Color.R * 299 + background.Color.G * 587 + background.Color.B * 114 < 128000;
        DwmWindowFrame.Apply(_windowSource.Handle, border.Color, dark);
    }

    /// <summary>窗口销毁时解除原生消息订阅。</summary>
    protected override void OnClosed(EventArgs args)
    {
        if (_windowSource is { IsDisposed: false })
            _windowSource.RemoveHook(OnWindowMessage);
        _windowSource = null;
        StateChanged -= OnWindowStateChanged;
        base.OnClosed(args);
    }

    /// <summary>最大化时使用实际工作区，避免标题栏裁切和底栏覆盖任务栏。</summary>
    private static nint OnWindowMessage(nint handle, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0024) // WM_GETMINMAXINFO
            WindowWorkArea.ApplyMaximizedBounds(handle, lParam);
        // 不设置 handled，保留 WPF 对 MinWidth、MinHeight 和拖拽尺寸的约束。
        return nint.Zero;
    }

    /// <summary>调整大小模式变化后同步原生边缘和命令可用状态。</summary>
    private static void OnResizeModeChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        ((HermodWindow)target).UpdateWindowChrome();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>拒绝负值及非有限高度，避免布局和原生命中测试范围不一致。</summary>
    private static bool IsValidCaptionHeight(object value) => value is double height
        && double.IsFinite(height) && height >= 0;

    /// <summary>运行时调整标题高度后立即同步 Chrome，模板通过绑定自动重新布局。</summary>
    private static void OnCaptionHeightChanged(DependencyObject target, DependencyPropertyChangedEventArgs args) =>
        ((HermodWindow)target).UpdateWindowChrome();

    /// <summary>状态变化只调整拖拽边缘，圆角由桌面合成器随窗口状态处理。</summary>
    private void OnWindowStateChanged(object? sender, EventArgs args)
    {
        UpdateWindowChrome();
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>只为可调整大小的正常窗口保留六个逻辑像素的原生拖拽边缘。</summary>
    private void UpdateWindowChrome()
    {
        // 基类构造期间也可能触发依赖属性回调，此时字段尚未初始化。
        if (_chrome is null)
            return;
        var maximized = WindowState == WindowState.Maximized;
        var resizable = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        _chrome.CaptionHeight = CaptionHeight;
        _chrome.ResizeBorderThickness = new Thickness(resizable && !maximized ? 6 : 0);
    }

    /// <summary>不可调整大小的模态弹窗不提供最小化。</summary>
    private void OnCanMinimize(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = ResizeMode != ResizeMode.NoResize;
        args.Handled = true;
    }

    /// <summary>只有可调整大小的窗口支持最大化。</summary>
    private void OnCanMaximize(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        args.Handled = true;
    }

    /// <summary>最小化或最大化的窗口可还原，正常状态无需执行。</summary>
    private void OnCanRestore(object sender, CanExecuteRoutedEventArgs args)
    {
        args.CanExecute = ResizeMode != ResizeMode.NoResize && WindowState != WindowState.Normal;
        args.Handled = true;
    }

    /// <summary>通过原生窗口状态完成最小化。</summary>
    private void OnMinimize(object sender, ExecutedRoutedEventArgs args)
    {
        WindowState = WindowState.Minimized;
        args.Handled = true;
    }

    /// <summary>最大化命令再次执行时还原，便于现有自定义标题栏继续使用。</summary>
    private void OnMaximize(object sender, ExecutedRoutedEventArgs args)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        args.Handled = true;
    }

    /// <summary>恢复 WPF 保存的正常窗口位置和尺寸。</summary>
    private void OnRestore(object sender, ExecutedRoutedEventArgs args)
    {
        WindowState = WindowState.Normal;
        args.Handled = true;
    }

    /// <summary>交给 WPF 触发 Closing 和模态结果处理，业务清理由派生窗口负责。</summary>
    private void OnClose(object sender, ExecutedRoutedEventArgs args)
    {
        Close();
        args.Handled = true;
    }
}
