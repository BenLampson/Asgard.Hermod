namespace Asgard.Hermod.UI.Controls;

/// <summary>独立工作窗口的七十二像素标题；模块只定义身份、状态与操作内容。</summary>
public class WorkspaceHeader : Control
{
    /// <summary>类型图标、名称及安全端点。</summary>
    public static readonly DependencyProperty IdentityContentProperty = DependencyProperty.Register(nameof(IdentityContent), typeof(object), typeof(WorkspaceHeader));
    /// <summary>实际连接与环境状态。</summary>
    public static readonly DependencyProperty StatusContentProperty = DependencyProperty.Register(nameof(StatusContent), typeof(object), typeof(WorkspaceHeader));
    /// <summary>模块自定义操作，不包含原生窗口按钮。</summary>
    public static readonly DependencyProperty ActionsContentProperty = DependencyProperty.Register(nameof(ActionsContent), typeof(object), typeof(WorkspaceHeader));
    /// <summary>共享连接状态控制恢复操作。</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(WorkspaceConnectionViewModel), typeof(WorkspaceHeader));
    /// <summary>协议模块提供的明确重连入口。</summary>
    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand), typeof(WorkspaceHeader));
    /// <summary>协议模块提供的编辑连接入口。</summary>
    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(nameof(EditCommand), typeof(ICommand), typeof(WorkspaceHeader));
    /// <summary>只有外层工作窗口展示窗口按钮；嵌入式内容不重复显示。</summary>
    public static readonly DependencyProperty ShowWindowControlsProperty = DependencyProperty.Register(nameof(ShowWindowControls), typeof(bool), typeof(WorkspaceHeader));
    private static readonly DependencyPropertyKey IsCompactPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsCompact), typeof(bool), typeof(WorkspaceHeader), new PropertyMetadata(false));
    /// <summary>模块可让次要状态让位，实际连接状态保持常驻。</summary>
    public static readonly DependencyProperty IsCompactProperty = IsCompactPropertyKey.DependencyProperty;
    /// <summary>紧凑标题的模块呈现提示。</summary>
    public bool IsCompact => (bool)GetValue(IsCompactProperty);
    /// <summary>实例创建后按已应用的启动语言读取，避免主题模板缓存首次解析的语种。</summary>
    public string EditConnectionLabel => Text.Get("WorkspaceConnectionEdit");
    /// <summary>重连文案属于标题实例，不固化到共享模板。</summary>
    public string ReconnectLabel => Text.Get("WorkspaceConnectionRetry");
    /// <summary>身份内容。</summary>
    public object? IdentityContent { get => GetValue(IdentityContentProperty); set => SetValue(IdentityContentProperty, value); }
    /// <summary>状态内容。</summary>
    public object? StatusContent { get => GetValue(StatusContentProperty); set => SetValue(StatusContentProperty, value); }
    /// <summary>模块操作。</summary>
    public object? ActionsContent { get => GetValue(ActionsContentProperty); set => SetValue(ActionsContentProperty, value); }
    /// <summary>连接状态。</summary>
    public WorkspaceConnectionViewModel? State { get => (WorkspaceConnectionViewModel?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>重连动作。</summary>
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }
    /// <summary>编辑动作。</summary>
    public ICommand? EditCommand { get => (ICommand?)GetValue(EditCommandProperty); set => SetValue(EditCommandProperty, value); }
    /// <summary>是否显示固定窗口按钮。</summary>
    public bool ShowWindowControls { get => (bool)GetValue(ShowWindowControlsProperty); set => SetValue(ShowWindowControlsProperty, value); }
    /// <summary>空白区域保留 WindowChrome 原生拖动，操作槽显式参与命中。</summary>
    static WorkspaceHeader() => DefaultStyleKeyProperty.OverrideMetadata(typeof(WorkspaceHeader), new FrameworkPropertyMetadata(typeof(WorkspaceHeader)));
    /// <summary>标题槽是可命名的模板内容；模块的原有名称域保持不变。</summary>
    public WorkspaceHeader() => SizeChanged += OnHeaderSizeChanged;
    /// <summary>模板加载后重新应用宽窄布局。</summary>
    public override void OnApplyTemplate() { base.OnApplyTemplate(); RefreshHeaderWidth(); }
    private void OnHeaderSizeChanged(object sender, SizeChangedEventArgs args) => RefreshHeaderWidth();
    private void RefreshHeaderWidth()
    {
        SetValue(IsCompactPropertyKey, ActualWidth < 1000);
        if (GetTemplateChild("IdentityColumn") is ColumnDefinition identity)
            identity.Width = new(ActualWidth < 900 ? 190 : 240);
    }
}
