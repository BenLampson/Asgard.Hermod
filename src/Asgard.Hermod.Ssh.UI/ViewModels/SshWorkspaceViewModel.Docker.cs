using System.Text;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>Docker 命令库属于当前 SSH 工作区，复用已验证传输和独立通道。</summary>
public sealed partial class SshWorkspaceViewModel
{
    private readonly SshDockerEnvironmentReader _dockerEnvironmentReader = new();
    /// <summary>命令参数及执行结果属于当前实例窗口。</summary>
    public SshDockerCommandsViewModel DockerCommands { get; private set; } = null!;
    /// <summary>隐藏命令库不会取消正在执行的操作。</summary>
    public bool IsDockerCommandsOpen
    {
        get => ActiveSection == "docker";
        set { if (value) ActiveSection = "docker"; else if (IsDockerCommandsOpen) ActiveSection = "terminal"; }
    }
    private bool _dockerMonitorStarted;
    /// <summary>Docker 资源采样只属于管理页，不改变实例监测所选栏目。</summary>
    public SshMonitorViewModel DockerMonitor { get; private set; } = null!;
    /// <summary>资源面板收起后暂停采样；管理页保留其历史和选中容器。</summary>
    public bool IsDockerResourcesOpen
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            DockerMonitor.IsPaused = !value;
            DockerMonitor.IsPanelVisible = value;
        }
    }
    /// <summary>在 Docker 管理内部打开资源监测。</summary>
    public AsyncRelayCommand ShowDockerResourcesCommand { get; private set; } = null!;
    /// <summary>标题栏打开或收起命令库。</summary>
    public AsyncRelayCommand ToggleDockerCommandsCommand { get; private set; } = null!;
    /// <summary>返回监控或终端，不改变连接。</summary>
    public RelayCommand CloseDockerCommandsCommand { get; private set; } = null!;

    private void InitializeDockerCommands()
    {
        DockerMonitor = new(_dockerEnvironmentReader) { Section = "docker", IsPaused = true };
        DockerCommands = new(() => IsConnected && !IsClosed ? _connection as ISshWorkspaceConnection : null, () => RemoteIdentity, _dialogs,
            () => !IsReadOnly, RunDockerInTerminalAsync, _dockerEnvironmentReader);
        ToggleDockerCommandsCommand = new AsyncRelayCommand(async _ =>
        {
            IsDockerCommandsOpen = !IsDockerCommandsOpen;
            if (IsDockerCommandsOpen) await InitializeDockerPanelAsync();
        }, _ => !IsClosed);
        CloseDockerCommandsCommand = new RelayCommand(_ => IsDockerCommandsOpen = false);
        ShowDockerResourcesCommand = new AsyncRelayCommand(async _ =>
        {
            if (_connection is not ISshWorkspaceConnection connection || !IsConnected) return;
            IsDockerCommandsOpen = true; IsDockerResourcesOpen = true;
            if (!_dockerMonitorStarted)
            {
                _dockerMonitorStarted = true;
                await DockerMonitor.StartAsync(connection);
            }
            else await DockerMonitor.RefreshDetailsCommand.ExecuteAsync();
            if (!IsDockerCommandsOpen || !IsConnected || !ReferenceEquals(connection, _connection))
            { IsDockerResourcesOpen = false; DockerMonitor.IsPaused = true; DockerMonitor.IsPanelVisible = false; return; }
            if (DockerCommands.SelectedContainer is { } selected) DockerMonitor.SelectDockerContainer(selected.Id);
        }, _ => !IsClosed && IsConnected);
        DockerMonitor.DockerOperationRequested += async (_, operation) =>
        {
            if (!IsConnected || IsClosed) return;
            IsDockerResourcesOpen = false;
            if (DockerCommands.IsBusy) { IsDockerCommandsOpen = true; return; }
            var container = DockerMonitor.SelectedContainer;
            DockerCommands.Select(operation == "manage" ? "logs" : operation, container); IsDockerCommandsOpen = true;
            await DockerCommands.InitializeAsync();
            if (operation is "start" or "stop" or "restart") await DockerCommands.RunActionAsync(operation);
        };
        DockerMonitor.ContainerLogsRequested += OnContainerLogsRequested;
        DockerCommands.Changed += (_, _) => { if (IsDockerResourcesOpen) DockerMonitor.RefreshDetailsCommand.Execute(null); };
        DockerCommands.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SshDockerCommandsViewModel.SelectedContainer) && DockerCommands.SelectedContainer is { } selected)
                DockerMonitor.SelectDockerContainer(selected.Id);
        };
        DockerMonitor.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SshMonitorViewModel.ResultCount) && IsDockerCommandsOpen)
                DockerCommands.UpdateResources(DockerMonitor.Containers);
        };
    }

    /// <summary>打开管理工具时自动初始化环境与独立采样，不替换交互终端。</summary>
    private async Task InitializeDockerPanelAsync()
    {
        await DockerCommands.InitializeAsync();
        if (IsDockerCommandsOpen && DockerCommands.IsInitialized) await ShowDockerResourcesCommand.ExecuteAsync();
    }

    private Task RunDockerInTerminalAsync(SshDockerCommandPlan plan, CancellationToken token)
    {
        var task = OpenDockerConsoleAsync(plan, token);
        _consoleOpenTask = TrackDockerConsoleAsync(task); return task;
    }

    private static async Task TrackDockerConsoleAsync(Task task)
    {
        // 失败由命令库呈现；生命周期等待只负责收尾，不能再次向窗口关闭路径抛出同一异常。
        try { await task; } catch (Exception) { }
    }

    private async Task OpenDockerConsoleAsync(SshDockerCommandPlan plan, CancellationToken token)
    {
        if (_connection is not { } connection || !IsConnected || IsCreatingConsole || IsClosed) throw Text.Exception("SshWorkspaceUnavailable");
        IsCreatingConsole = true;
        try
        {
            var opened = await connection.OpenConsoleAsync(120, 32, token).WaitAsync(token);
            if (token.IsCancellationRequested || IsClosed || !IsConnected || !ReferenceEquals(connection, _connection))
            { await opened.DisposeAsync(); throw new OperationCanceledException(token); }
            var console = AddOpenedConsole(opened, 120, 32);
            // 新 PTY 不继承已有控制台未完成的输入，仅派发固定构建器产生的单行命令。
            console.Session.SendInput(Encoding.UTF8.GetBytes(plan.Command + "\r"));
        }
        finally { IsCreatingConsole = false; }
    }
}
