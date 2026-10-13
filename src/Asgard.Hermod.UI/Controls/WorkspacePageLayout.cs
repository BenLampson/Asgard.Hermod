namespace Asgard.Hermod.UI.Controls;

/// <summary>类型无关的内容页布局；工具栏、通知、正文、详情和状态栏均由模块提供。</summary>
public class WorkspacePageLayout : ContentControl
{
    /// <summary>顶部工具栏插槽。</summary>
    public static readonly DependencyProperty ToolbarProperty = DependencyProperty.Register(
        nameof(Toolbar), typeof(object), typeof(WorkspacePageLayout));
    /// <summary>正文上方的通知插槽。</summary>
    public static readonly DependencyProperty NotificationProperty = DependencyProperty.Register(
        nameof(Notification), typeof(object), typeof(WorkspacePageLayout));
    /// <summary>按需展开的详情正文。</summary>
    public static readonly DependencyProperty DetailsProperty = DependencyProperty.Register(
        nameof(Details), typeof(object), typeof(WorkspacePageLayout));
    /// <summary>底部状态栏插槽。</summary>
    public static readonly DependencyProperty StatusContentProperty = DependencyProperty.Register(
        nameof(StatusContent), typeof(object), typeof(WorkspacePageLayout));
    /// <summary>详情显隐由调用者负责，不改变其业务状态。</summary>
    public static readonly DependencyProperty IsDetailsOpenProperty = DependencyProperty.Register(
        nameof(IsDetailsOpen), typeof(bool), typeof(WorkspacePageLayout), new PropertyMetadata(false));
    /// <summary>宽窗口详情栏宽度，窄窗口使用同宽覆盖层。</summary>
    public static readonly DependencyProperty DetailsWidthProperty = DependencyProperty.Register(
        nameof(DetailsWidth), typeof(double), typeof(WorkspacePageLayout), new PropertyMetadata(300d));
    /// <summary>低于此宽度时将详情切换为覆盖层，正文继续使用全部宽度。</summary>
    public static readonly DependencyProperty CompactBreakpointProperty = DependencyProperty.Register(
        nameof(CompactBreakpoint), typeof(double), typeof(WorkspacePageLayout), new PropertyMetadata(760d, OnBreakpointChanged));
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsCompact), typeof(bool), typeof(WorkspacePageLayout), new PropertyMetadata(true));
    /// <summary>布局计算出的紧凑状态，供模块折叠次要操作。</summary>
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;

    /// <summary>从公共资源加载布局，不要求协议模块复制 Grid 或样式。</summary>
    static WorkspacePageLayout() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspacePageLayout),
        new FrameworkPropertyMetadata(typeof(WorkspacePageLayout)));

    /// <summary>尺寸变化只更新展示状态，终端或编辑器自行响应实际正文尺寸。</summary>
    public WorkspacePageLayout() => SizeChanged += (_, _) => RefreshCompactState();

    /// <summary>工具栏内容。</summary>
    public object? Toolbar { get => GetValue(ToolbarProperty); set => SetValue(ToolbarProperty, value); }
    /// <summary>通知内容。</summary>
    public object? Notification { get => GetValue(NotificationProperty); set => SetValue(NotificationProperty, value); }
    /// <summary>详情内容。</summary>
    public object? Details { get => GetValue(DetailsProperty); set => SetValue(DetailsProperty, value); }
    /// <summary>状态栏内容。</summary>
    public object? StatusContent { get => GetValue(StatusContentProperty); set => SetValue(StatusContentProperty, value); }
    /// <summary>详情是否展开。</summary>
    public bool IsDetailsOpen { get => (bool)GetValue(IsDetailsOpenProperty); set => SetValue(IsDetailsOpenProperty, value); }
    /// <summary>详情栏宽度。</summary>
    public double DetailsWidth { get => (double)GetValue(DetailsWidthProperty); set => SetValue(DetailsWidthProperty, value); }
    /// <summary>紧凑模式断点。</summary>
    public double CompactBreakpoint { get => (double)GetValue(CompactBreakpointProperty); set => SetValue(CompactBreakpointProperty, value); }
    /// <summary>当前是否使用紧凑布局。</summary>
    public bool IsCompact => (bool)GetValue(IsCompactProperty);

    /// <summary>调用者调整断点后同步更新布局。</summary>
    private static void OnBreakpointChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((WorkspacePageLayout)owner).RefreshCompactState();
    /// <summary>覆盖式详情不减少正文宽度，避免窄窗口挤没终端。</summary>
    private void RefreshCompactState() => SetValue(IsCompactPropertyKey, ActualWidth < CompactBreakpoint);
}
