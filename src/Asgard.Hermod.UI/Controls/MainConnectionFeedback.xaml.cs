namespace Asgard.Hermod.UI.Controls;

/// <summary>首次连接的主窗体覆盖层，不构造工作窗口；只有已显示的活动窗口才接收键盘焦点。</summary>
public partial class MainConnectionFeedback : UserControl
{
    /// <summary>构造纯布局，不连接服务器或创建 HWND。</summary>
    public MainConnectionFeedback() => InitializeComponent();
    private void OnKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key != Key.Escape || DataContext is not MainConnectionFeedbackViewModel feedback) return;
        args.Handled = true;
        var command = feedback.IsConnecting ? feedback.CancelCommand : feedback.DismissCommand;
        if (command.CanExecute(null)) command.Execute(null);
    }
    private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs args)
    {
        if (args.NewValue is not true) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible || Window.GetWindow(this)?.IsActive != true) return;
            (DataContext is MainConnectionFeedbackViewModel { IsConnecting: true } ? CancelConnection : RetryConnection).Focus();
        }, DispatcherPriority.Input);
    }
}
