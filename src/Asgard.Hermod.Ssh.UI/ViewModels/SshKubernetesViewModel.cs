using System.Collections.ObjectModel;
using System.Windows.Data;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>当前 SSH 实例的 kubectl 管理；连接失效时撤销旧查询和确认，输出与终端输入隔离。</summary>
public sealed partial class SshKubernetesViewModel : ObservableObject, IDisposable
{
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly Func<string> _host;
    private readonly ISshWorkspaceDialogs _dialogs;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;
    private bool _initializing;
    private readonly Dictionary<string, KubernetesEnvironmentSnapshot> _snapshots = new(StringComparer.Ordinal);

    /// <summary>复用已验证 SSH 连接，通过独立 exec 通道读取远端 kubectl。</summary>
    public SshKubernetesViewModel(Func<ISshWorkspaceConnection?> connection, Func<string> host, ISshWorkspaceDialogs dialogs)
    {
        _connection = connection; _host = host; _dialogs = dialogs;
        ItemView = CollectionViewSource.GetDefaultView(Items);
        ItemView.Filter = item => item is SshKubernetesResource resource && (Search.Length == 0 || resource.Name.Contains(Search, StringComparison.OrdinalIgnoreCase));
        DetectCommand = new AsyncRelayCommand(_ => DetectAsync(), _ => CanEdit);
        RefreshCommand = new AsyncRelayCommand(_ => DetectAsync(), _ => CanQuery);
        ActionCommand = new AsyncRelayCommand(action => RunActionAsync(action as string ?? ""), action => CanRun(action as string));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
    }

    /// <summary>上下文名称来自远端配置，不读证书或令牌。</summary>
    public ObservableCollection<string> Contexts { get; } = [];
    /// <summary>命名空间名称来自显式选定的集群。</summary>
    public ObservableCollection<string> Namespaces { get; } = [];
    /// <summary>真实查询结果；切换范围立即清空旧目标。</summary>
    public ObservableCollection<SshKubernetesResource> Items { get; } = [];
    /// <summary>资源搜索仅筛选本地结果，不发送终端命令。</summary>
    public ICollectionView ItemView { get; }
    /// <summary>列表名称筛选；筛掉的目标不能继续操作。</summary>
    public string Search { get; set { if (SetProperty(ref field, value)) { ItemView.Refresh(); if (SelectedItem is { } item && !ItemView.Contains(item)) SelectedItem = null; OnPropertyChanged(nameof(HasNoItems)); } } } = "";
    /// <summary>完整初始化成功后重开工具不会重复请求。</summary>
    public bool IsInitialized { get; private set => SetProperty(ref field, value); }
    /// <summary>命名空间列表失败只显示安全提示，不重试网络请求。</summary>
    public string NamespaceWarning { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasNamespaceWarning)); } } = "";
    /// <summary>空提示不占用管理面板的垂直空间。</summary>
    public bool HasNamespaceWarning => NamespaceWarning.Length > 0;
    /// <summary>仅在失败时展示错误栏。</summary>
    public bool HasError => Error.Length > 0;
    /// <summary>固定资源目录的本地化展示。</summary>
    public IReadOnlyList<KubernetesResourceChoice> ResourceChoices { get; } = SshKubernetesCommands.Resources.Select(id => new KubernetesResourceChoice(id, Text.Get("KubeResource_" + id))).ToArray();
    /// <summary>当前上下文只影响本页命令，不改写服务器默认上下文。</summary>
    public string Context { get; set { if (SetProperty(ref field, value ?? "")) { ApplyContextSnapshot(); } } } = "";
    /// <summary>列表权限不足时仍可手动输入已知命名空间。</summary>
    public string Namespace { get; set { if (SetProperty(ref field, value)) InvalidateScope(); } } = "default";
    /// <summary>稳定资源类型，不比较翻译标签。</summary>
    public string Resource { get; set { if (SshKubernetesCommands.Resources.Contains(value) && SetProperty(ref field, value)) InvalidateScope(); } } = "pods";
    /// <summary>仅 Deployment 展示发布状态和滚动重启，Pod 操作栏保持精简。</summary>
    public bool IsDeploymentResource => Resource == "deployments";
    /// <summary>只有当前查询列表中的目标才允许执行动作。</summary>
    public SshKubernetesResource? SelectedItem { get; set { if (SetProperty(ref field, value)) Notify(); } }
    /// <summary>同一时间只执行一个查询或变更。</summary>
    public bool IsBusy { get; private set { if (SetProperty(ref field, value)) Notify(); } }
    /// <summary>客户端存在与集群权限分别处理。</summary>
    public bool IsInstalled { get; private set { if (SetProperty(ref field, value)) Notify(); } }
    /// <summary>仅显示已解析客户端版本。</summary>
    public string EnvironmentSummary { get; private set => SetProperty(ref field, value); } = Text.Get("KubeDetectHint");
    /// <summary>本页始终标明当前 SSH 目标。</summary>
    public string Host => _host();
    /// <summary>完整资源句式的安全错误。</summary>
    public string Error { get; private set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(HasError)); } } = "";
    /// <summary>查询或动作结果状态。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>显式成功查询的终端输出不翻译；失败的 SDK/命令响应不显示。</summary>
    public string Output { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>输出绑定执行时的固定目标，不随下拉框变化误标结果。</summary>
    public string ResultTarget { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>可复制的最后执行命令，不包含认证凭据。</summary>
    public string ExecutedCommand { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>结果标签绑定实际读取动作，不随范围编辑误标已捕获输出。</summary>
    public string OutputSection { get; private set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(IsLogOutput)); OnPropertyChanged(nameof(IsDetailsOutput)); OnPropertyChanged(nameof(IsEventsOutput)); } } } = "describe";
    /// <summary>当前捕获的是日志。</summary>
    public bool IsLogOutput => OutputSection == "logs";
    /// <summary>当前捕获的是资源详情。</summary>
    public bool IsDetailsOutput => OutputSection == "describe";
    /// <summary>当前捕获的是资源事件。</summary>
    public bool IsEventsOutput => OutputSection == "events";
    /// <summary>参数可编辑状态。</summary>
    public bool CanEdit => !_disposed && !IsBusy && _connection() is not null;
    /// <summary>读取资源需要已检测客户端和明确的上下文。</summary>
    public bool CanQuery => CanEdit && IsInstalled && Context.Length > 0;
    /// <summary>没有列表权限时保留具体错误提示。</summary>
    public bool HasNoItems => ItemView.IsEmpty && !IsBusy;
    /// <summary>重新检测远端客户端和配置。</summary>
    public AsyncRelayCommand DetectCommand { get; }
    /// <summary>显式重新加载环境快照，不后台轮询集群。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>详情、日志、发布状态和滚动重启。</summary>
    public AsyncRelayCommand ActionCommand { get; }
    /// <summary>取消只关闭当前命令通道，变更已派发时提示结果未知。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>仅供显式加载动作调用；打开、重连和切换筛选条件不会调用。</summary>
    public Task InitializeAsync() => !IsInitialized ? DetectAsync() : Task.CompletedTask;

    private bool CanRun(string? action) => CanQuery && SelectedItem is { } item && Items.Contains(item) && action switch
    { "describe" => true, "logs" => Resource == "pods", "events" => Resource is "pods" or "deployments" or "services", "rollout" or "restart" => Resource == "deployments", _ => false };

    private async Task RunActionAsync(string action)
    {
        if (!CanRun(action) || SelectedItem is not { } item) return;
        string context = Context, ns = item.Namespace.Length > 0 ? item.Namespace : Namespace, resource = Resource;
        string command;
        try { command = SshKubernetesCommands.Action(context, ns, resource, item.Name, action); }
        catch (Exception error) { Error = Text.Error(error, "KubeInvalidParameters"); return; }
        await RunAsync(async (connection, generation, token) =>
        {
            if (action == "restart")
            {
                bool approved = await _dialogs.ConfirmKubernetesActionAsync(Text.Get("KubeRestart"),
                    Text.Format("KubeRestartImpact", item.Name, ns, context, Host), command, token).WaitAsync(token);
                EnsureCurrent(connection, generation, token);
                if (!approved) { Status = Text.Get("DockerDeclined"); return; }
            }
            if (context != Context || resource != Resource || !ReferenceEquals(item, SelectedItem) || !Items.Contains(item)) return;
            OutputSection = action is "logs" or "events" ? action : "describe";
            Output = ""; ExecutedCommand = command; ResultTarget = Text.Format("KubeResultTarget", item.Name, ns, context, Host);
            bool dispatched = false;
            try
            {
                dispatched = true;
                var result = await ExecuteAsync(connection, command, generation, token);
                if (action == "restart" && result.ExitCode == -1 && result.Truncated) throw Text.Exception("KubeOutcomeUnknown");
                if (action == "restart" && result.ExitCode != 0) { Error = Text.Get("KubeQueryFailed"); return; }
                RequireSuccess(result, allowTruncated: action != "restart");
                Output = action == "restart" ? Text.Format("KubeRestarted", item.Name) : result.Output;
                Status = Text.Get(result.Truncated ? "SshOutputTruncated" : "KubeCompleted");
            }
            catch (Exception) when (action == "restart" && dispatched && generation == _generation)
            { Error = Text.Get("KubeOutcomeUnknown"); }
        });
    }

    private async Task RunAsync(Func<ISshWorkspaceConnection, long, CancellationToken, Task> run)
    {
        if (!CanEdit || _connection() is not { } connection) return;
        long generation = _generation;
        using var pending = new CancellationTokenSource(TimeSpan.FromMinutes(5)); _pending = pending;
        IsBusy = true; Error = ""; Status = Text.Get("KubeWorking");
        try { await run(connection, generation, pending.Token); }
        catch (OperationCanceledException) { if (generation == _generation) Error = Text.Get("KubeCanceled"); }
        catch (Exception error) { if (generation == _generation) Error = Text.Error(error, "KubeQueryFailed"); }
        finally
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
                if (generation == _generation && Error.Length > 0) Status = "";
                IsBusy = false; Notify();
            }
        }
    }

    private async Task<SshCommandResult> ExecuteAsync(ISshWorkspaceConnection connection, string command, long generation, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var result = await connection.ExecuteCommandAsync(command, timeout.Token).WaitAsync(timeout.Token);
        EnsureCurrent(connection, generation, token); return result;
    }
    private static void RequireSuccess(SshCommandResult result, bool allowTruncated = false)
    {
        if (result.ExitCode != 0) throw Text.Exception("KubeQueryFailed");
        if (!allowTruncated && result.Truncated) throw Text.Exception("KubeTooManyResources");
    }
    private void EnsureCurrent(ISshWorkspaceConnection connection, long generation, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_disposed || generation != _generation || !ReferenceEquals(connection, _connection())) throw new OperationCanceledException(token);
    }
    /// <summary>选择变化只筛选已加载快照，绝不发起远端请求。</summary>
    private void InvalidateScope()
    {
        Items.Clear(); SelectedItem = null; Error = ""; Status = ""; Notify();
        if (!_initializing) ApplyResourceSnapshot();
    }
    private void Notify()
    {
        foreach (string property in new[] { nameof(CanEdit), nameof(CanQuery), nameof(HasNoItems), nameof(IsDeploymentResource) }) OnPropertyChanged(property);
        DetectCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); ActionCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>断线清空旧上下文和目标，迟到结果不能进入新连接。</summary>
    public void ConnectionChanged()
    {
        _generation++; _pending?.Cancel(); _pending = null; IsBusy = false; _initializing = false; _snapshots.Clear();
        IsInstalled = false; IsInitialized = false; Context = ""; Contexts.Clear(); Namespaces.Clear();
        Items.Clear(); SelectedItem = null; Output = ""; ExecutedCommand = ""; ResultTarget = "";
        Error = ""; EnvironmentSummary = Text.Get("KubeDetectHint"); Notify();
    }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) return; _disposed = true; ConnectionChanged(); }
}
