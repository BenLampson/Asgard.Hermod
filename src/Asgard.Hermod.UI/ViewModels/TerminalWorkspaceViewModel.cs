namespace Asgard.Hermod.UI.ViewModels;

/// <summary>管理终端工作页句柄；显示控件只绑定会话，不负责创建进程。</summary>
public sealed class TerminalWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly ITerminalSessionFactory _factory;
    private readonly Dispatcher _dispatcher;
    private ITerminalWorkspaceSession? _session;
    private string _status = Text.Get("Starting");
    private string? _error;
    private bool _isRunning;
    /// <inheritdoc />
    public override WorkspaceConnectionViewModel ConnectionState { get; } = new();

    /// <summary>用户打开页签时显式创建本地终端。</summary>
    public TerminalWorkspaceViewModel(string id, InstanceItemViewModel instance, ITerminalSessionFactory factory)
        : base(id, "PowerShell", "terminal", instance.Id)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Instance = instance;
        RetryCommand = new AsyncRelayCommand(_ => RetryAsync(), _ => CanRetry);
        StartSession();
    }

    /// <summary>实例名称。</summary>
    public string Name => Instance.Name;
    /// <summary>只读标题复用主窗体的实例快照，不持有连接配置。</summary>
    public InstanceItemViewModel Instance { get; }
    /// <summary>当前终端会话；关闭开始时同步变为 null。</summary>
    public TerminalSession? Session => IsClosed ? null : _session?.Terminal;
    /// <summary>当前 Shell 类型。</summary>
    public string Subtitle => Text.Format("LocalTerminalSubtitle", _session?.DisplayName ?? Text.Get("WindowsPowerShell"));
    /// <summary>进程状态或退出码。</summary>
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    /// <summary>可供用户阅读的启动或通信错误。</summary>
    public string? Error { get => _error; private set => SetProperty(ref _error, value); }
    /// <summary>是否仍在运行。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value))
                return;
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(IsReadOnly));
            RetryCommand.NotifyCanExecuteChanged();
        }
    }
    /// <summary>退出或启动失败后允许重新打开。</summary>
    public bool CanRetry => !IsClosed && !IsRunning;
    /// <summary>退出后保留输出与复制，输入不能进入已经结束的进程。</summary>
    public bool IsReadOnly => !IsRunning;
    /// <summary>先释放旧句柄再重新创建会话。</summary>
    public AsyncRelayCommand RetryCommand { get; }

    /// <summary>在创建边界处理错误，失败页仍可关闭或重试。</summary>
    private void StartSession()
    {
        if (IsClosed)
            return;
        Error = null;
        try
        {
            _session = _factory.OpenLocal();
            Status = Text.Get("Running");
            IsRunning = true; HasConnectedOnce = true; ConnectionState.Finish();
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(Subtitle));
            _ = ObserveCompletionAsync(_session);
        }
        catch (Exception exception)
        {
            _session = null;
            Status = Text.Get("FailedToStart");
            IsRunning = false;
            Error = Text.Error(exception, "CannotStartTheTerminalCheckTheShellAndAccessPermissionsThenTryAgain");
            ConnectionState.Finish(Error);
            OnPropertyChanged(nameof(Session));
            OnPropertyChanged(nameof(CanRetry));
            OnPropertyChanged(nameof(IsReadOnly));
            RetryCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>进程退出后保留最后输出，直到用户关闭或重试。</summary>
    private async Task ObserveCompletionAsync(ITerminalWorkspaceSession observed)
    {
        string status;
        string? error = null;
        try
        {
            int exitCode = await observed.Completion.ConfigureAwait(false);
            status = Text.Format("Exited", exitCode);
        }
        catch (Exception exception)
        {
            status = Text.Get("Disconnected");
            error = Text.Error(exception, "TheTerminalConnectionWasInterruptedOpenANewSessionOrTryAgain");
        }
        if (_dispatcher.HasShutdownStarted)
            return;
        await _dispatcher.InvokeAsync(() =>
        {
            if (IsClosed || !ReferenceEquals(_session, observed))
                return;
            Error = error;
            Status = status;
            IsRunning = false;
            ConnectionState.Disconnected(error ?? status);
        });
    }

    /// <summary>重试前先解除显示绑定并等待原连接释放。</summary>
    private async Task RetryAsync()
    {
        if (_session is { } previous)
        {
            _session = null;
            OnPropertyChanged(nameof(Session));
            await previous.DisposeAsync();
        }
        StartSession();
    }

    /// <summary>关闭时先解除控件绑定，再等待 ConPTY 输出排空。</summary>
    protected override async ValueTask ReleaseAsync()
    {
        OnPropertyChanged(nameof(Session));
        IsRunning = false;
        RetryCommand.NotifyCanExecuteChanged();
        if (_session is { } session)
        {
            _session = null;
            await session.DisposeAsync();
        }
    }
}
