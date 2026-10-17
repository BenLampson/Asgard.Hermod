namespace Asgard.Hermod.UI.Views.Dialogs;

/// <summary>使用共享主题和密码控件注册账号，关闭后取消请求。</summary>
public partial class AccountRegistrationWindow : Controls.HermodDialog
{
    private readonly AccountRegistrationViewModel _model;
    /// <summary>窗口仅负责焦点与模态结果，不持有服务凭据。</summary>
    public AccountRegistrationWindow(AccountRegistrationViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model; InitializeComponent(); DataContext = model;
        model.CloseRequested += OnCloseRequested;
        Loaded += OnLoaded;
        Closed += OnClosed;
    }
    /// <summary>键盘从用户名开始。</summary>
    private void OnLoaded(object sender, RoutedEventArgs args) => UsernameInput.Focus();
    /// <summary>只有点击登录才进入原有浏览器流程。</summary>
    private void OnCloseRequested(object? sender, bool signIn) => DialogResult = signIn;
    /// <summary>关闭时立即取消，并解除窗口事件订阅。</summary>
    private void OnClosed(object? sender, EventArgs args)
    {
        _model.Cancel(); _model.CloseRequested -= OnCloseRequested;
        Loaded -= OnLoaded; Closed -= OnClosed;
    }
}
