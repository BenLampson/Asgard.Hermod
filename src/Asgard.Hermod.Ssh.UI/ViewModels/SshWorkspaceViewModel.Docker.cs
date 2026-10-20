using System.Text;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>Docker 命令库属于当前 SSH 工作区，复用已验证传输和独立通道。</summary>
public sealed partial class SshWorkspaceViewModel
{
    private readonly SshDockerEnvironmentReader _dockerEnvironmentReader = new();
    /// <summary>命令参数及执行结果属于当前实例窗口。</summary>
    public SshDockerCommandsViewModel DockerCommands { get; private set; } = null!;
    /// <summary>隐藏命令库不会取消正在执行的操作。</summary>
    public bool IsDockerCommandsOpen { get; set { if (SetProperty(ref field, value)) OnPropertyChanged(nameof(IsTerminalSection)); } }
    /// <summary>标题栏打开或收起命令库。</summary>
    public AsyncRelayCommand ToggleDockerCommandsCommand { get; private set; } = null!;
    /// <summary>返回监控或终端，不改变连接。</summary>
    public RelayCommand CloseDockerCommandsCommand { get; private set; } = null!;

    private void InitializeDockerCommands()
    {
        DockerCommands = new(() => IsConnected && !IsClosed ? _connection as ISshWorkspaceConnection : null, () => RemoteIdentity, _dialogs,
            () => !IsReadOnly, RunDockerInTerminalAsync, _dockerEnvironmentReader);
        ToggleDockerCommandsCommand = new AsyncRelayCommand(async _ =>
        {
            IsDockerCommandsOpen = !IsDockerCommandsOpen;
            if (IsDockerCommandsOpen) await DockerCommands.InitializeAsync();
        }, _ => !IsClosed);
        CloseDockerCommandsCommand = new RelayCommand(_ => IsDockerCommandsOpen = false);
        Monitor.DockerOperationRequested += async (_, operation) =>
        {
            if (!IsConnected || IsClosed) return;
            if (DockerCommands.IsBusy) { IsDockerCommandsOpen = true; return; }
            var container = Monitor.SelectedContainer;
            DockerCommands.Select(operation == "manage" ? "logs" : operation, container); IsDockerCommandsOpen = true;
            await DockerCommands.InitializeAsync();
            if (operation is "start" or "stop" or "restart") await DockerCommands.RunActionAsync(operation);
        };
        DockerCommands.Changed += (_, _) => Monitor.RefreshDetailsCommand.Execute(null);
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
            IsDockerCommandsOpen = false;
            // 新 PTY 不继承已有控制台未完成的输入，仅派发固定构建器产生的单行命令。
            console.Session.SendInput(Encoding.UTF8.GetBytes(plan.Command + "\r"));
        }
        finally { IsCreatingConsole = false; }
    }
}
