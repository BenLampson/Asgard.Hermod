using System.Collections.ObjectModel;
using System.Text.Json;
using Asgard.Hermod.Ssh.UI.Services;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>当前 SSH 实例的 kubectl 管理；连接失效时撤销旧查询和确认，输出与终端输入隔离。</summary>
public sealed class SshKubernetesViewModel : ObservableObject, IDisposable
{
    private readonly Func<ISshWorkspaceConnection?> _connection;
    private readonly Func<string> _host;
    private readonly ISshWorkspaceDialogs _dialogs;
    private CancellationTokenSource? _pending;
    private long _generation;
    private bool _disposed;

    /// <summary>复用已验证 SSH 连接，通过独立 exec 通道读取远端 kubectl。</summary>
    public SshKubernetesViewModel(Func<ISshWorkspaceConnection?> connection, Func<string> host, ISshWorkspaceDialogs dialogs)
    {
        _connection = connection; _host = host; _dialogs = dialogs;
        DetectCommand = new AsyncRelayCommand(_ => DetectAsync(), _ => CanEdit);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => CanQuery);
        ActionCommand = new AsyncRelayCommand(action => RunActionAsync(action as string ?? ""), action => CanRun(action as string));
        CancelCommand = new RelayCommand(_ => _pending?.Cancel(), _ => IsBusy);
    }

    /// <summary>上下文名称来自远端配置，不读证书或令牌。</summary>
    public ObservableCollection<string> Contexts { get; } = [];
    /// <summary>命名空间名称来自显式选定的集群。</summary>
    public ObservableCollection<string> Namespaces { get; } = [];
    /// <summary>真实查询结果；切换范围立即清空旧目标。</summary>
    public ObservableCollection<SshKubernetesResource> Items { get; } = [];
    /// <summary>固定资源目录的本地化展示。</summary>
    public IReadOnlyList<KubernetesResourceChoice> ResourceChoices { get; } = SshKubernetesCommands.Resources.Select(id => new KubernetesResourceChoice(id, Text.Get("KubeResource_" + id))).ToArray();
    /// <summary>当前上下文只影响本页命令，不改写服务器默认上下文。</summary>
    public string Context { get; set { if (SetProperty(ref field, value)) { Namespaces.Clear(); Namespace = "default"; InvalidateScope(); } } } = "";
    /// <summary>列表权限不足时仍可手动输入已知命名空间。</summary>
    public string Namespace { get; set { if (SetProperty(ref field, value)) InvalidateScope(); } } = "default";
    /// <summary>稳定资源类型，不比较翻译标签。</summary>
    public string Resource { get; set { if (SshKubernetesCommands.Resources.Contains(value) && SetProperty(ref field, value)) InvalidateScope(); } } = "pods";
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
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>查询或动作结果状态。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>显式成功查询的终端输出不翻译；失败的 SDK/命令响应不显示。</summary>
    public string Output { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>输出绑定执行时的固定目标，不随下拉框变化误标结果。</summary>
    public string ResultTarget { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>可复制的最后执行命令，不包含认证凭据。</summary>
    public string ExecutedCommand { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>参数可编辑状态。</summary>
    public bool CanEdit => !_disposed && !IsBusy && _connection() is not null;
    /// <summary>读取资源需要已检测客户端和明确的上下文。</summary>
    public bool CanQuery => CanEdit && IsInstalled && Context.Length > 0;
    /// <summary>没有列表权限时保留具体错误提示。</summary>
    public bool HasNoItems => Items.Count == 0;
    /// <summary>重新检测远端客户端和配置。</summary>
    public AsyncRelayCommand DetectCommand { get; }
    /// <summary>显式刷新当前范围，不后台轮询集群。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>详情、日志、发布状态和滚动重启。</summary>
    public AsyncRelayCommand ActionCommand { get; }
    /// <summary>取消只关闭当前命令通道，变更已派发时提示结果未知。</summary>
    public RelayCommand CancelCommand { get; }

    /// <summary>第一次打开自动检测，再次打开保留当前范围与结果。</summary>
    public Task InitializeAsync() => !IsInstalled ? DetectAsync() : Task.CompletedTask;

    private Task DetectAsync() => RunAsync(async (connection, generation, token) =>
    {
        IsInstalled = false; Contexts.Clear(); Namespaces.Clear(); Context = ""; Items.Clear();
        var probe = await ExecuteAsync(connection, SshKubernetesCommands.Probe, generation, token);
        if (probe.ExitCode == 127) { EnvironmentSummary = Text.Get("KubeMissing"); Status = ""; return; }
        RequireSuccess(probe);
        using var version = JsonDocument.Parse(probe.Output);
        string clientVersion = version.RootElement.GetProperty("clientVersion").GetProperty("gitVersion").GetString() ?? "";
        IsInstalled = true; EnvironmentSummary = Text.Format("KubeDetected", clientVersion);
        var contexts = await ExecuteAsync(connection, SshKubernetesCommands.Contexts, generation, token); RequireSuccess(contexts);
        foreach (string name in contexts.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).Take(1000)) Contexts.Add(name);
        var current = await ExecuteAsync(connection, SshKubernetesCommands.CurrentContext, generation, token);
        string preferred = current.ExitCode == 0 && !current.Truncated ? current.Output.Trim() : "";
        Context = Contexts.Contains(preferred) ? preferred : Contexts.FirstOrDefault() ?? "";
        Status = Text.Get(Context.Length == 0 ? "KubeNoContext" : "KubeReady");
    });

    private Task RefreshAsync()
    {
        string context = Context, ns = Namespace, resource = Resource;
        return RunAsync(async (connection, generation, token) =>
        {
            // 先撤销旧目标，读取失败不能继续操作此前的列表。
            Items.Clear(); SelectedItem = null;
            string command = SshKubernetesCommands.List(context, ns, resource);
            var result = await ExecuteAsync(connection, command, generation, token); RequireSuccess(result);
            if (context != Context || ns != Namespace || resource != Resource) return;
            var items = SshKubernetesResource.Parse(result.Output);
            foreach (var item in items) Items.Add(item);
            Status = Text.Format("KubeLoaded", Items.Count);
            if (Namespaces.Count == 0)
            {
                // 集群 namespace 列表权限不足不阻止用户查询已知命名空间。
                var namespaces = await ExecuteAsync(connection, SshKubernetesCommands.List(context, ns, "namespaces"), generation, token);
                if (context != Context || ns != Namespace || resource != Resource) return;
                if (namespaces.ExitCode == 0 && !namespaces.Truncated)
                    foreach (var item in SshKubernetesResource.Parse(namespaces.Output)) Namespaces.Add(item.Name);
            }
        });
    }

    private bool CanRun(string? action) => CanQuery && SelectedItem is { } item && Items.Contains(item) && action switch
    { "describe" => true, "logs" => Resource == "pods", "rollout" or "restart" => Resource == "deployments", _ => false };

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
        using var pending = new CancellationTokenSource(TimeSpan.FromSeconds(60)); _pending = pending;
        IsBusy = true; Error = ""; Status = Text.Get("KubeWorking");
        try { await run(connection, generation, pending.Token); }
        catch (OperationCanceledException) { if (generation == _generation) Error = Text.Get("KubeCanceled"); }
        catch (Exception error) { if (generation == _generation) Error = Text.Error(error, "KubeQueryFailed"); }
        finally
        {
            if (ReferenceEquals(_pending, pending)) _pending = null;
            if (generation == _generation && Error.Length > 0) Status = "";
            IsBusy = false; Notify();
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
    private void InvalidateScope() { Items.Clear(); SelectedItem = null; Error = ""; Status = Text.Get("KubeRefreshHint"); Notify(); }
    private void Notify()
    {
        foreach (string property in new[] { nameof(CanEdit), nameof(CanQuery), nameof(HasNoItems) }) OnPropertyChanged(property);
        DetectCommand?.NotifyCanExecuteChanged(); RefreshCommand?.NotifyCanExecuteChanged(); ActionCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged();
    }
    /// <summary>断线清空旧上下文和目标，迟到结果不能进入新连接。</summary>
    public void ConnectionChanged()
    {
        _generation++; _pending?.Cancel(); IsInstalled = false; Context = ""; Contexts.Clear(); Namespaces.Clear();
        Items.Clear(); SelectedItem = null; Output = ""; ExecutedCommand = ""; ResultTarget = "";
        Error = ""; EnvironmentSummary = Text.Get("KubeDetectHint"); Notify();
    }
    /// <inheritdoc />
    public void Dispose() { if (_disposed) return; _disposed = true; ConnectionChanged(); }
}
