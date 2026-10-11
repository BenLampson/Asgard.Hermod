namespace Asgard.Hermod.WinTerminal;

/// <summary>
/// 原生 WPF 终端控件；宿主拥有 Session，移出可视树仅暂停绘制。
/// </summary>
public sealed partial class TerminalControl : Control, IDisposable
{
    /// <summary>声明显示会话的依赖属性。</summary>
    public static readonly DependencyProperty SessionProperty = DependencyProperty.Register(
        nameof(Session), typeof(TerminalSession), typeof(TerminalControl),
        new FrameworkPropertyMetadata(null, OnSessionChanged));

    private readonly TerminalRenderer _renderer = new();
    private readonly DispatcherTimer _renderTimer;
    private readonly DispatcherTimer _resizeTimer;
    private TerminalSession? _session;
    private TerminalSelection? _selection;
    private (int X, int Y)? _selectionAnchor;
    private TerminalMouseButton? _pressedButton;
    private int _scrollOffset;
    private int _pendingRender = 1;
    private long _renderedRevision = -1;
    private bool _fontConfigured;
    private bool _disposed;

    /// <summary>获取或设置显示会话；设置为 null 时解除事件订阅。</summary>
    public TerminalSession? Session
    {
        get => (TerminalSession?)GetValue(SessionProperty);
        set
        {
            ObjectDisposedException.ThrowIf(_disposed && value is not null, this);
            SetValue(SessionProperty, value);
        }
    }

    /// <summary>完成一次输出重绘调度后在 UI 线程触发。</summary>
    public event EventHandler? ScreenUpdated;

    /// <summary>配置原生输入、裁剪和默认字体。</summary>
    static TerminalControl()
    {
        FocusableProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(true));
        ClipToBoundsProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(true));
        FontFamilyProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(new FontFamily("Consolas")));
        FontSizeProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(14.0));
        BackgroundProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(Brushes.Black));
        ForegroundProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(Brushes.White));
        KeyboardNavigation.TabNavigationProperty.OverrideMetadata(typeof(TerminalControl), new FrameworkPropertyMetadata(KeyboardNavigationMode.None));
    }

    /// <summary>创建绘制与尺寸防抖计时器；仅在 Loaded 后运行。</summary>
    public TerminalControl()
    {
        _renderTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(33),
        };
        _resizeTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _renderTimer.Tick += OnRenderTick;
        _resizeTimer.Tick += OnResizeTick;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>切换会话时解除旧订阅并清空与旧缓冲区关联的选区。</summary>
    private static void OnSessionChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (TerminalControl)sender;
        if (control._session is { } oldSession)
            oldSession.BufferUpdated -= control.OnBufferUpdated;
        control._session = (TerminalSession?)args.NewValue;
        if (control._session is { } newSession)
            newSession.BufferUpdated += control.OnBufferUpdated;
        control._selection = null;
        control._scrollOffset = 0;
        control._renderedRevision = -1;
        control._fontConfigured = false;
        control.ConfigureRenderer();
        control.ResizeToBounds();
        control.InvalidateVisual();
    }

    /// <summary>从后台线程合并输出通知，避免每个数据块占用一次 Dispatcher 调度。</summary>
    private void OnBufferUpdated(object? sender, EventArgs args) => Interlocked.Exchange(ref _pendingRender, 1);

    /// <summary>进入可视树时恢复重绘与网格尺寸。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_disposed)
            return;
        ConfigureRenderer();
        ResizeToBounds();
        _renderTimer.Start();
        InvalidateVisual();
    }

    /// <summary>移出可视树时暂停计时器，保留会话与字体缓存供再次挂载。</summary>
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _renderTimer.Stop();
        _resizeTimer.Stop();
    }

    /// <summary>字体与颜色改变后重新配置渲染器。</summary>
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs args)
    {
        base.OnPropertyChanged(args);
        if (_renderer is null || _disposed)
            return;
        if (args.Property == FontFamilyProperty || args.Property == FontSizeProperty
            || args.Property == ForegroundProperty || args.Property == BackgroundProperty)
        {
            _fontConfigured = false;
            ConfigureRenderer();
            ResizeToBounds();
            InvalidateVisual();
        }
    }

    /// <summary>关闭宿主时显式释放控件；会话和连接器仍由宿主负责。</summary>
    public void Dispose()
    {
        VerifyAccess();
        if (_disposed)
            return;
        _disposed = true;
        _renderTimer.Stop();
        _resizeTimer.Stop();
        _renderTimer.Tick -= OnRenderTick;
        _resizeTimer.Tick -= OnResizeTick;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;
        Session = null;
        _renderer.Dispose();
        ScreenUpdated = null;
        GC.SuppressFinalize(this);
    }
}
