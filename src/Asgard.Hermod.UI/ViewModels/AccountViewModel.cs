namespace Asgard.Hermod.UI.ViewModels;

/// <summary>统一呈现账号操作状态；窗口关闭会取消并等待本次网络操作。</summary>
public sealed class AccountViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAccountService _accounts;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _operation = Task.CompletedTask;
    private string _password = "";
    private string _message = "";
    private bool _isBusy;
    private bool _confirmRestore;
    private string? _identity;

    /// <summary>注入公共服务，不读取令牌或数据库。</summary>
    public AccountViewModel(IAccountService accounts, IWorkspaceSyncService sync)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sync);
        _accounts = accounts;
        _identity = accounts.Account?.SubjectId;
        SignInCommand = new AsyncRelayCommand(_ => RunAsync(() => accounts.SignInAsync(_lifetime.Token), "LoginSucceeded"), _ => !IsBusy && !_lifetime.IsCancellationRequested);
        SignOutCommand = new AsyncRelayCommand(_ => RunAsync(() => accounts.SignOutAsync(_lifetime.Token), "SignedOut"), _ => !IsBusy && IsSignedIn && !_lifetime.IsCancellationRequested);
        UploadCommand = new AsyncRelayCommand(_ => RunAsync(() => sync.UploadAsync(Password, _lifetime.Token), "SyncUploadSucceeded"), _ => CanSync);
        DownloadCommand = new AsyncRelayCommand(_ => RunAsync(() => sync.DownloadAsync(Password, _lifetime.Token), "SyncDownloadSucceeded"), _ => CanSync && ConfirmRestore);
        CancelCommand = new RelayCommand(_ => _lifetime.Cancel(), _ => IsBusy);
        accounts.AccountChanged += OnAccountChanged;
    }
    /// <summary>只显示 JWT 用户名，未登录时显示资源文案。</summary>
    public string Username => _accounts.Account?.Username ?? Text.Get("NotSignedIn");
    /// <summary>当前登录状态。</summary>
    public bool IsSignedIn => _accounts.Account is not null;
    /// <summary>登录后明确提供切换账号，避免继续显示登录按钮造成状态歧义。</summary>
    public string SignInButtonText => Text.Get(IsSignedIn ? "SwitchAccount" : "SignInToYourAccount");
    /// <summary>账号窗口同时显示用户名与认证状态。</summary>
    public string AccountStatus => Text.Get(IsSignedIn ? "SignedIn" : "NotSignedIn");
    /// <summary>互斥操作执行状态。</summary>
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); RefreshCommands(); } }
    /// <summary>本次窗口内存中的密码，关闭后清除且不保存。</summary>
    public string Password { get => _password; set { SetProperty(ref _password, value); RefreshCommands(); } }
    /// <summary>用户明确确认下载将替换本地实例与标签。</summary>
    public bool ConfirmRestore { get => _confirmRestore; set { SetProperty(ref _confirmRestore, value); RefreshCommands(); } }
    /// <summary>安全的进度与结果，禁止展示原始异常。</summary>
    public string Message
    {
        get => _message;
        private set { if (SetProperty(ref _message, value)) OnPropertyChanged(nameof(HasMessage)); }
    }
    /// <summary>没有操作结果时收起固定状态区，不占用表单空间。</summary>
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    /// <summary>用稳定状态控制错误强调，不通过翻译后的提示文本判断结果。</summary>
    public bool IsError { get; private set => SetProperty(ref field, value); }
    /// <summary>浏览器登录与账号切换。</summary>
    public AsyncRelayCommand SignInCommand { get; }
    /// <summary>退出本应用。</summary>
    public AsyncRelayCommand SignOutCommand { get; }
    /// <summary>主动上传加密快照。</summary>
    public AsyncRelayCommand UploadCommand { get; }
    /// <summary>主动恢复云端快照。</summary>
    public AsyncRelayCommand DownloadCommand { get; }
    /// <summary>取消当前操作；取消后关闭窗口再重新打开可以重试。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>有效登录和密码长度共同控制同步入口。</summary>
    private bool CanSync => IsSignedIn && !IsBusy && !_lifetime.IsCancellationRequested && Password.Length is >= 12 and <= 128;
    /// <summary>窗口范围内共享一份可等待操作，切换账号时清除密码和恢复确认。</summary>
    private Task RunAsync(Func<Task> action, string success)
    {
        _operation = ExecuteAsync(action, success);
        return _operation;
    }
    /// <summary>对取消、安全业务错误和未知网络错误分别给出本地化反馈。</summary>
    private async Task ExecuteAsync(Func<Task> action, string success)
    {
        IsError = false; IsBusy = true; Message = Text.Get("AccountOperationInProgress");
        try { await action(); Message = Text.Get(success); }
        catch (OperationCanceledException) { Message = Text.Get("AccountOperationCancelled"); }
        catch (Exception error) { IsError = true; Message = Text.Error(error, "AccountOperationFailed"); }
        finally { IsBusy = false; }
    }
    /// <summary>服务事件发生在窗口 Dispatcher 的异步上下文中。</summary>
    private void OnAccountChanged(object? sender, EventArgs args)
    {
        OnPropertyChanged(nameof(Username)); OnPropertyChanged(nameof(IsSignedIn));
        OnPropertyChanged(nameof(SignInButtonText)); OnPropertyChanged(nameof(AccountStatus));
        // 同一身份刷新不清除当前操作密码，真正切换或退出时不向下一账号遗留秘密。
        if (_identity != _accounts.Account?.SubjectId)
        {
            _identity = _accounts.Account?.SubjectId;
            Password = ""; ConfirmRestore = false;
        }
        RefreshCommands();
    }
    /// <summary>互斥状态统一更新所有按钮。</summary>
    private void RefreshCommands()
    {
        SignInCommand.NotifyCanExecuteChanged(); SignOutCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged(); DownloadCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _operation;
        _accounts.AccountChanged -= OnAccountChanged;
        Password = "";
        _lifetime.Dispose();
    }
}
