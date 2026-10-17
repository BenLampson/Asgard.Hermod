namespace Asgard.Hermod.UI.ViewModels;

/// <summary>统一呈现账号操作状态；窗口关闭会取消并等待本次网络操作。</summary>
public sealed class AccountViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAccountService _accounts;
    private readonly IConfigurationSyncService? _automatic;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _operation = Task.CompletedTask;
    private string _message = "";
    private bool _isBusy;
    private readonly bool _registrationAvailable;

    /// <summary>注入公共服务，不读取令牌或数据库。</summary>
    public AccountViewModel(IAccountService accounts, IWorkspaceSyncService sync, Func<bool, string?> requestPassword,
        IConfigurationSyncService? automatic = null,
        Func<IReadOnlyList<ConfigurationConflict>, IReadOnlyDictionary<string, bool>?>? requestMerge = null,
        Func<Task<bool>>? requestRegistration = null)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(sync);
        ArgumentNullException.ThrowIfNull(requestPassword);
        _accounts = accounts;
        _automatic = automatic;
        _registrationAvailable = requestRegistration is not null;
        SignInCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
        {
            await accounts.SignInAsync(_lifetime.Token);
            if (automatic is null) return;
            await automatic.PrepareWorkspaceAsync(_lifetime.Token);
            if (!automatic.HasPassword) await EnableAutomaticAsync();
            else await automatic.SynchronizeAsync(_lifetime.Token);
        }, "LoginSucceeded"), _ => !IsBusy && !_lifetime.IsCancellationRequested);
        SignOutCommand = new AsyncRelayCommand(_ => RunAsync(() => accounts.SignOutAsync(_lifetime.Token), "SignedOut"), _ => !IsBusy && IsSignedIn && !_lifetime.IsCancellationRequested);
        RegisterCommand = new AsyncRelayCommand(async _ =>
        {
            if (await requestRegistration!()) await SignInCommand.ExecuteAsync();
        }, _ => HasRegistration && !IsBusy && !_lifetime.IsCancellationRequested);
        UploadCommand = new AsyncRelayCommand(_ => RunAsync(() => SynchronizeAsync(false), "SyncUploadSucceeded"), _ => CanSync);
        DownloadCommand = new AsyncRelayCommand(_ => RunAsync(() => SynchronizeAsync(true), "SyncDownloadSucceeded"), _ => CanSync);
        CancelCommand = new RelayCommand(_ => _lifetime.Cancel(), _ => IsBusy);
        EnableSyncCommand = new AsyncRelayCommand(_ => RunAsync(EnableAutomaticAsync, "SyncEnabled"), _ => CanSync && automatic is not null);
        SyncNowCommand = new AsyncRelayCommand(_ => RunAsync(() => automatic!.SynchronizeAsync(_lifetime.Token), "SyncChecked"), _ => CanSync && automatic?.HasPassword == true);
        ForgetSyncPasswordCommand = new AsyncRelayCommand(_ => RunAsync(() => automatic!.ForgetPasswordAsync(_lifetime.Token), "SyncPasswordForgotten"), _ => CanSync && automatic?.HasPassword == true);
        MergeCommand = new AsyncRelayCommand(_ => RunAsync(async () =>
        {
            var choices = requestMerge?.Invoke(automatic!.Conflicts);
            if (choices is not null) await automatic!.ResolveAsync(choices, _lifetime.Token);
        }, "SyncChecked"), _ => CanSync && automatic?.Conflicts.Count > 0 && requestMerge is not null);
        accounts.AccountChanged += OnAccountChanged;
        if (automatic is not null) automatic.Changed += OnSyncChanged;

        // 只在首次解锁时索取密码，取消保持登录和待同步内容。
        async Task EnableAutomaticAsync()
        {
            if (automatic is null) return;
            var account = accounts.Account;
            string? password = requestPassword(false);
            try
            {
                if (password is null) return;
                if (account != accounts.Account) throw new OperationCanceledException();
                await automatic.EnableAsync(password, _lifetime.Token);
                await automatic.SynchronizeAsync(_lifetime.Token);
            }
            finally { password = null; }
        }

        // 密码只属于这次同步操作；模态窗口取消后不访问云端。
        async Task SynchronizeAsync(bool download)
        {
            var account = accounts.Account;
            string? password = requestPassword(download);
            try
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (password is null || account != accounts.Account) throw new OperationCanceledException();
                if (download) await sync.DownloadAsync(password, _lifetime.Token);
                else await sync.UploadAsync(password, _lifetime.Token);
            }
            finally { password = null; }
        }
    }
    /// <summary>只显示 JWT 用户名，未登录时显示资源文案。</summary>
    public string Username => _accounts.Account?.Username ?? Text.Get("NotSignedIn");
    /// <summary>当前登录状态。</summary>
    public bool IsSignedIn => _accounts.Account is not null;
    /// <summary>未登录时展示普通账号注册入口。</summary>
    public bool HasRegistration => _registrationAvailable && !IsSignedIn;
    /// <summary>打开注册表单，不自动提交登录。</summary>
    public AsyncRelayCommand RegisterCommand { get; }
    /// <summary>登录后明确提供切换账号，避免继续显示登录按钮造成状态歧义。</summary>
    public string SignInButtonText => Text.Get(IsSignedIn ? "SwitchAccount" : "SignInToYourAccount");
    /// <summary>账号窗口同时显示用户名与认证状态。</summary>
    public string AccountStatus => Text.Get(IsSignedIn ? "SignedIn" : "NotSignedIn");
    /// <summary>生产界面使用自动同步面板，旧接口仅为兼容旧客户端保留。</summary>
    public bool HasAutomaticSync => _automatic is not null;
    public bool HasManualSync => _automatic is null;
    public string SyncMessage => _automatic?.Message ?? "";
    public string LastSyncText => _automatic?.LastSyncedAt is { } value ? Text.Format("SyncLastSuccess", value.ToLocalTime()) : Text.Get("SyncNeverSucceeded");
    public bool HasConflicts => _automatic?.Conflicts.Count > 0;
    public AsyncRelayCommand EnableSyncCommand { get; }
    public AsyncRelayCommand SyncNowCommand { get; }
    public AsyncRelayCommand ForgetSyncPasswordCommand { get; }
    public AsyncRelayCommand MergeCommand { get; }
    /// <summary>互斥操作执行状态。</summary>
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); RefreshCommands(); } }
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
    /// <summary>登录后即可打开密码弹窗，正在执行时禁止其他账号操作。</summary>
    private bool CanSync => IsSignedIn && !IsBusy && !_lifetime.IsCancellationRequested;
    /// <summary>窗口范围内共享一份可等待操作。</summary>
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
        OnPropertyChanged(nameof(HasRegistration));
        RefreshCommands();
    }
    /// <summary>互斥状态统一更新所有按钮。</summary>
    private void RefreshCommands()
    {
        SignInCommand.NotifyCanExecuteChanged(); SignOutCommand.NotifyCanExecuteChanged();
        RegisterCommand.NotifyCanExecuteChanged();
        UploadCommand.NotifyCanExecuteChanged(); DownloadCommand.NotifyCanExecuteChanged(); CancelCommand.NotifyCanExecuteChanged();
        EnableSyncCommand.NotifyCanExecuteChanged(); SyncNowCommand.NotifyCanExecuteChanged();
        ForgetSyncPasswordCommand.NotifyCanExecuteChanged(); MergeCommand.NotifyCanExecuteChanged();
    }
    /// <summary>后台服务通知通过 Dispatcher 更新绑定。</summary>
    private void OnSyncChanged(object? sender, EventArgs args)
    {
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        { dispatcher.BeginInvoke(() => OnSyncChanged(sender, args)); return; }
        OnPropertyChanged(nameof(SyncMessage)); OnPropertyChanged(nameof(LastSyncText)); OnPropertyChanged(nameof(HasConflicts)); RefreshCommands();
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        await _operation;
        _accounts.AccountChanged -= OnAccountChanged;
        if (_automatic is not null) _automatic.Changed -= OnSyncChanged;
        _lifetime.Dispose();
    }
}
