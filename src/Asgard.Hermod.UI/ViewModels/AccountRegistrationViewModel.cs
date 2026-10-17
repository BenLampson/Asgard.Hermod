namespace Asgard.Hermod.UI.ViewModels;

/// <summary>普通账号注册表单；密码只在本次窗口内存在，结果未知时禁止重复提交。</summary>
public sealed class AccountRegistrationViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IAccountRegistrationService _service;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _operation = Task.CompletedTask;
    private bool _disposed;

    /// <summary>注册成功仍等待用户明确选择登录，不创建或切换当前会话。</summary>
    public AccountRegistrationViewModel(IAccountRegistrationService service)
    {
        ArgumentNullException.ThrowIfNull(service);
        _service = service;
        RegisterCommand = new AsyncRelayCommand(_ => _operation = RegisterAsync(), _ => CanRegister);
        CloseCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, false), _ => !_disposed);
        SignInCommand = new RelayCommand(_ => CloseRequested?.Invoke(this, true), _ => !IsBusy && (IsRegistered || IsUncertain) && !_disposed);
    }
    /// <summary>通知窗口关闭；true 表示用户明确选择继续既有浏览器登录。</summary>
    public event EventHandler<bool>? CloseRequested;
    /// <summary>未登录身份的登录名输入。</summary>
    public string Username { get; set => SetProperty(ref field, value); } = "";
    /// <summary>登录邮箱输入。</summary>
    public string Email { get; set => SetProperty(ref field, value); } = "";
    /// <summary>不持久化的初始密码。</summary>
    public string Password { get; set => SetProperty(ref field, value); } = "";
    /// <summary>只在客户端核对的重复密码，不发送给 API。</summary>
    public string ConfirmPassword { get; set => SetProperty(ref field, value); } = "";
    /// <summary>请求执行中禁止修改或重复提交资料。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>明确确认创建成功。</summary>
    public bool IsRegistered { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>断网等情况下无法确认是否已创建，先尝试登录。</summary>
    public bool IsUncertain { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>提交之后的安全、本地化状态。</summary>
    public string Message { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>错误强调使用稳定布尔状态。</summary>
    public bool IsError { get; private set => SetProperty(ref field, value); }
    /// <summary>成功、未知结果或关闭后不再发送创建请求。</summary>
    public bool CanRegister => !_disposed && !IsBusy && !IsRegistered && !IsUncertain;
    /// <summary>资料输入在提交和成功后保持只读。</summary>
    public bool CanEdit => CanRegister;
    /// <summary>创建普通账号。</summary>
    public AsyncRelayCommand RegisterCommand { get; }
    /// <summary>关闭并取消在途操作。</summary>
    public RelayCommand CloseCommand { get; }
    /// <summary>明确选择使用已创建账号登录。</summary>
    public RelayCommand SignInCommand { get; }

    /// <summary>一次提交；无论结果如何均清除窗口中的密码引用。</summary>
    private async Task RegisterAsync()
    {
        IsBusy = true; IsError = false; Message = Text.Get("RegistrationInProgress");
        try
        {
            var request = new AccountRegistrationRequest { Username = Username, Email = Email, Password = Password };
            request.Validate();
            if (Password != ConfirmPassword) throw Text.Exception("RegistrationPasswordMismatch");
            var created = await _service.RegisterAsync(request, _lifetime.Token);
            _lifetime.Token.ThrowIfCancellationRequested();
            Username = created.Username; IsRegistered = true;
            Message = Text.Get("RegistrationSucceeded");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception error)
        {
            IsError = true;
            IsUncertain = error.Data["Hermod.ResourceKey"] is not string key || key == "RegistrationOutcomeUnknown";
            Message = Text.Error(error, "RegistrationOutcomeUnknown");
        }
        finally { Password = ""; ConfirmPassword = ""; IsBusy = false; }
    }
    /// <summary>字段编辑和提交命令共享同一状态。</summary>
    private void RefreshCommands()
    {
        OnPropertyChanged(nameof(CanRegister)); OnPropertyChanged(nameof(CanEdit));
        RegisterCommand.NotifyCanExecuteChanged(); SignInCommand.NotifyCanExecuteChanged();
    }
    /// <summary>窗口关闭即取消网络请求；释放时等待完成并清除密码。</summary>
    public void Cancel() { if (!_disposed) _lifetime.Cancel(); }
    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel();
        await _operation;
        Password = ""; ConfirmPassword = "";
        _lifetime.Dispose(); RefreshCommands(); CloseCommand.NotifyCanExecuteChanged();
    }
}
