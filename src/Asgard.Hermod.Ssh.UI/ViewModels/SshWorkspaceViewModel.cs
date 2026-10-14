using System.Globalization;
using System.Threading.Channels;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>独立拥有 SSH 会话与终端缓冲区，切换标签页不会释放连接。</summary>
public sealed partial class SshWorkspaceViewModel : WorkspacePageViewModel
{
    private readonly IInstanceConfigurationService _configurations;
    private readonly InstanceItemViewModel _instance;
    private readonly ISshConnectionFactory _connections;
    private readonly IInstanceEditor _editor;
    private readonly ISshWorkspaceDialogs _dialogs;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _saveFeedbackTimer;
    private CancellationTokenSource? _connecting;
    private CancellationTokenSource? _ioCancellation;
    private ISshConnection? _connection;
    private TerminalSession _terminal = new();
    private Channel<ReadOnlyMemory<byte>>? _input;
    private Task? _connectTask;
    private Task? _runTask;
    private bool _initialized;
    private bool _inputOverflow;
    private bool _disconnectRequested;
    private DateTime _connectedAt;
    private SshConnectionOptions? _profile;
    private SshWorkspacePhase _phase = SshWorkspacePhase.Connecting;

    /// <summary>页面只在首次激活或明确重连时建立连接。</summary>
    public SshWorkspaceViewModel(InstanceItemViewModel instance, IInstanceConfigurationService configurations,
        ISshConnectionFactory connections, IInstanceEditor editor, ISshWorkspaceDialogs dialogs)
        : base("ssh:" + Guid.NewGuid().ToString("N"), instance.Name, "terminal", instance.Id)
    {
        Name = instance.Name; _configurations = configurations; _connections = connections; _editor = editor; _dialogs = dialogs;
        _instance = instance;
        ReconnectCommand = new AsyncRelayCommand(_ => StartReconnectAsync(), _ => !IsClosed && IsEnded);
        DisconnectCommand = new AsyncRelayCommand(_ => DisconnectAsync(), _ => !IsClosed && IsConnected);
        CancelConnectionCommand = Connection.CancelCommand;
        ToggleDetailsCommand = new RelayCommand(_ => IsDetailsOpen = !IsDetailsOpen, _ => !IsClosed);
        EditConnectionCommand = new AsyncRelayCommand(_ => EditAsync(), _ => !IsClosed && !IsConnecting);
        NewSessionCommand = new RelayCommand(_ => RequestNewSession(), _ => !IsClosed);
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => OnPropertyChanged(nameof(Elapsed)), _dispatcher);
        _clock.Stop();
        _saveFeedbackTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background,
            (_, _) => ClearSaveFeedback(), _dispatcher);
        _saveFeedbackTimer.Stop();
    }

    /// <summary>当前实例显示名。</summary>
    public string Name { get; private set; }
    /// <summary>标题改名与侧栏共用安全元数据对象。</summary>
    public InstanceItemViewModel Instance => _instance;
    /// <summary>连接完成后替换缓冲区，失败时保留原输出；关闭时解绑。</summary>
    public TerminalSession? Session => IsClosed ? null : _terminal;
    /// <summary>稳定状态决定命令与输入。</summary>
    public SshWorkspacePhase Phase => _phase;
    /// <summary>连接中显示进度并允许取消。</summary>
    public bool IsConnecting => _phase == SshWorkspacePhase.Connecting;
    /// <summary>只有有效连接允许输入。</summary>
    public bool IsConnected => _phase == SshWorkspacePhase.Connected;
    /// <summary>建立连接、认证失败与运行中断统一使用公共弹窗。</summary>
    public WorkspaceConnectionViewModel Connection { get; } = new();
    /// <summary>终止状态保留输出和重连入口。</summary>
    public bool IsEnded => _phase is SshWorkspacePhase.Disconnected or SshWorkspacePhase.Failed;
    /// <summary>保持终端选择与复制功能，但拒绝断线后的输入。</summary>
    public bool IsReadOnly => IsClosed || !IsConnected;
    /// <summary>共享状态徽标文案。</summary>
    public string Status => Text.Get(IsConnecting ? "SshConnecting" : IsConnected ? "SshConnected" : "SshDisconnected");
    /// <summary>连接状态的共享语义色。</summary>
    public StatusTone Tone => IsConnected ? StatusTone.Success : IsConnecting ? StatusTone.Accent
        : Phase == SshWorkspacePhase.Failed ? StatusTone.Danger : StatusTone.Warning;
    /// <summary>公开远程身份，不含密码、私钥内容或代理口令。</summary>
    public string RemoteIdentity => _profile is null ? "" : _profile.Target.User + "@" + HostEndpoint;
    /// <summary>公开主机和端口，协议数字保持 invariant。</summary>
    public string HostEndpoint => _profile is null ? "" : _profile.Target.Host + ":" + _profile.Target.Port.ToString(CultureInfo.InvariantCulture);
    /// <summary>登录用户。</summary>
    public string UserName => _profile?.Target.User ?? "";
    /// <summary>连接中的完整句式标题。</summary>
    public string ConnectingTitle => Text.Format("SshStartingTitle", Name);
    /// <summary>安全反馈由协议错误资源生成。</summary>
    public string Error { get; private set; } = "";
    /// <summary>稳定错误码不包括 SDK 响应。</summary>
    public string ErrorCode { get; private set; } = "";
    /// <summary>结束状态以固定提示补充空错误。</summary>
    public string EndedMessage => Error.Length == 0 ? Text.Get("SshDisconnectedHint") : Error;
    /// <summary>已连接的公开主机身份，跳板与目标单独列出。</summary>
    public IReadOnlyList<TrustedHostKey> HostKeys { get; private set; } = [];
    /// <summary>当前路径的整句摘要。</summary>
    public string RouteDescription => _profile?.Route switch
    {
        SshIds.Jump => Text.Format("SshJumpRouteSummary", _profile.JumpHost!.Host, _profile.Target.Host),
        SshIds.Proxy => Text.Format("SshJumpRouteSummary", _profile.ProxyHost, _profile.Target.Host),
        _ => Text.Get("SshDirect"),
    };
    /// <summary>状态栏的路径标签。</summary>
    public string RouteLabel => Text.Get(_profile?.Route switch { SshIds.Jump => "SshViaJump", SshIds.Proxy => "SshViaProxy", _ => "SshDirectRoute" });
    /// <summary>连接存活时间只用于 UI，不存储为协议时间。</summary>
    public string Elapsed => IsConnected ? Text.Format("SshSessionElapsed", DateTime.UtcNow - _connectedAt) : Text.Get("SshSessionEnded");
    /// <summary>实际网格尺寸。</summary>
    public string GridSize => _terminal.Buffer.Columns.ToString(CultureInfo.InvariantCulture) + " × " + _terminal.Buffer.Rows.ToString(CultureInfo.InvariantCulture);
    /// <summary>按需展开的连接详情。</summary>
    public bool IsDetailsOpen { get; set => SetProperty(ref field, value); }
    /// <summary>保存成功的短暂提示不触碰当前连接与终端焦点。</summary>
    public bool HasSaveFeedback { get; private set => SetProperty(ref field, value); }
    /// <summary>显式建立一个新的会话，不重放旧输入。</summary>
    public AsyncRelayCommand ReconnectCommand { get; }
    /// <summary>断开后保留页签与输出。</summary>
    public AsyncRelayCommand DisconnectCommand { get; }
    /// <summary>取消网络和待核验的主机身份窗口。</summary>
    public RelayCommand CancelConnectionCommand { get; }
    /// <summary>连接详情显隐。</summary>
    public RelayCommand ToggleDetailsCommand { get; }
    /// <summary>编辑通过公共实例编辑器，下次连接生效。</summary>
    public AsyncRelayCommand EditConnectionCommand { get; }
    /// <summary>由外壳创建新的外层页签。</summary>
    public RelayCommand NewSessionCommand { get; }

    /// <inheritdoc />
    public override Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized || IsClosed) return _connectTask ?? Task.CompletedTask;
        _initialized = true;
        return _connectTask = ConnectAsync(cancellationToken);
    }
    /// <inheritdoc />
    public override Task<bool> RequestCloseAsync() => IsConnecting || IsConnected
        ? _dialogs.ConfirmCloseAsync(Name) : Task.FromResult(true);

    /// <summary>重新读取保存后的配置，旧会话结束后再创建新会话。</summary>
    private Task StartReconnectAsync() => _connectTask = ConnectAsync();

    /// <summary>提示自动收起，不切换页面或重建会话。</summary>
    private void ClearSaveFeedback() { HasSaveFeedback = false; _saveFeedbackTimer.Stop(); }

    /// <summary>编辑结果不修改现有传输，只更新实例名称。</summary>
    private async Task EditAsync()
    {
        try
        {
            HasSaveFeedback = false; _saveFeedbackTimer.Stop();
            if (await _editor.EditAsync(InstanceId!) is { } updated && !IsClosed)
            {
                _instance.UpdateSnapshot(updated); Name = updated.Name; Header = updated.Name; OnPropertyChanged(nameof(Name));
                HasSaveFeedback = true; _saveFeedbackTimer.Start();
            }
        }
        catch (Exception error) { Error = Text.Error(error, "SshConfigurationInvalid"); Refresh(); }
    }

    /// <summary>只在 UI 线程刷新绑定与命令。</summary>
    private void Refresh()
    {
        foreach (string name in new[] { nameof(Phase), nameof(IsConnecting), nameof(IsConnected), nameof(IsReadOnly), nameof(IsEnded), nameof(Status),
            nameof(Tone), nameof(RemoteIdentity), nameof(HostEndpoint), nameof(UserName), nameof(ConnectingTitle), nameof(Error), nameof(ErrorCode),
            nameof(EndedMessage), nameof(HostKeys), nameof(RouteDescription), nameof(RouteLabel), nameof(Elapsed), nameof(GridSize) }) OnPropertyChanged(name);
        ReconnectCommand.NotifyCanExecuteChanged(); DisconnectCommand.NotifyCanExecuteChanged(); CancelConnectionCommand.NotifyCanExecuteChanged();
        EditConnectionCommand.NotifyCanExecuteChanged(); NewSessionCommand.NotifyCanExecuteChanged(); ToggleDetailsCommand.NotifyCanExecuteChanged();
    }
}
