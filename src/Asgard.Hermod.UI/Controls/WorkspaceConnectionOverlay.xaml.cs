using Asgard.Hermod.UI.ViewModels;

namespace Asgard.Hermod.UI.Controls;

/// <summary>共用连接加载与失败卡片，使用覆盖层而非独立桌面窗口。</summary>
public partial class WorkspaceConnectionOverlay : UserControl
{
    /// <summary>模块提供连接状态。</summary>
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(WorkspaceConnectionViewModel), typeof(WorkspaceConnectionOverlay));
    /// <summary>连接目标的用户名称。</summary>
    public static readonly DependencyProperty TargetNameProperty = DependencyProperty.Register(nameof(TargetName), typeof(string), typeof(WorkspaceConnectionOverlay), new PropertyMetadata(""));
    /// <summary>模块控制显式重试。</summary>
    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(nameof(RetryCommand), typeof(ICommand), typeof(WorkspaceConnectionOverlay));
    /// <summary>模块控制编辑连接。</summary>
    public static readonly DependencyProperty EditCommandProperty = DependencyProperty.Register(nameof(EditCommand), typeof(ICommand), typeof(WorkspaceConnectionOverlay));
    /// <summary>连接状态。</summary>
    public WorkspaceConnectionViewModel? State { get => (WorkspaceConnectionViewModel?)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    /// <summary>目标名称，不显示连接凭据。</summary>
    public string TargetName { get => (string)GetValue(TargetNameProperty); set => SetValue(TargetNameProperty, value); }
    /// <summary>显式重试动作。</summary>
    public ICommand? RetryCommand { get => (ICommand?)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }
    /// <summary>编辑连接动作。</summary>
    public ICommand? EditCommand { get => (ICommand?)GetValue(EditCommandProperty); set => SetValue(EditCommandProperty, value); }
    /// <summary>创建真实模板，不触发连接。</summary>
    public WorkspaceConnectionOverlay() => InitializeComponent();
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || State is null) return;
        args.Handled = true; (State.IsConnecting ? State.CancelCommand : State.DismissCommand).Execute(null);
    }
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || Window.GetWindow(this)?.IsActive != true) return;
            (State?.IsConnecting == true ? CancelConnection : RetryConnection).Focus();
        }, DispatcherPriority.Input);
    }
}
