using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>工作区的文件、监测、转发和助手只共享连接，不共享交互 PTY 的字节流。</summary>
public sealed partial class SshWorkspaceViewModel
{
    private IAgentClient? _agentClient;
    private SshAssistantViewModel _emptyAssistant = null!;
    /// <summary>默认收起的 Agent，显隐不影响实例状态。</summary>
    public bool IsAgentOpen { get; set => SetProperty(ref field, value); }
    /// <summary>从标题栏显式打开监测，收起时不占终端高度。</summary>
    public bool IsMonitorOpen { get; set => SetProperty(ref field, value); }
    /// <summary>功能区的稳定 ID。</summary>
    public string ActiveSection
    {
        get;
        set
        {
            if (value is not ("terminal" or "files" or "forward" or "transfers" or "docker" or "kubernetes") || !SetProperty(ref field, value)) return;
            if (value != "docker") IsDockerResourcesOpen = false;
            foreach (string property in new[] { nameof(IsTerminalSection), nameof(IsFilesSection), nameof(IsForwardSection), nameof(IsTransfersSection), nameof(IsToolboxOpen),
                nameof(ToolboxTitle), nameof(IsDockerCommandsOpen), nameof(IsKubernetesOpen), nameof(IsManagementSection), nameof(IsBasicToolboxSection) }) OnPropertyChanged(property);
        }
    } = "terminal";
    /// <summary>终端可视状态。</summary>
    public bool IsTerminalSection => HasActiveConsole;
    /// <summary>基础设施工具与终端始终并排，窄窗口改为上下分栏。</summary>
    public bool IsManagementSection => IsDockerCommandsOpen || IsKubernetesOpen;
    /// <summary>普通工具使用共用标题，管理工具有自己的环境标题。</summary>
    public bool IsBasicToolboxSection => !IsManagementSection;
    /// <summary>实例工具箱显隐与控制台内容互不替换。</summary>
    public bool IsToolboxOpen => ActiveSection != "terminal";
    /// <summary>独立传输任务面板。</summary>
    public bool IsTransfersSection => ActiveSection == "transfers";
    /// <summary>当前工具箱标题。</summary>
    public string ToolboxTitle => Text.Get(ActiveSection switch { "files" => "SshFilesTitle", "forward" => "SshForwardTitle", "docker" => "DockerLibrary", "kubernetes" => "KubeTitle", _ => "SshFilesTransfers" });
    /// <summary>再次点击当前工具只收起面板，任务继续。</summary>
    public AsyncRelayCommand ToggleToolboxCommand { get; private set; } = null!;
    /// <summary>收起不停止实例任务。</summary>
    public RelayCommand CollapseToolboxCommand { get; private set; } = null!;
    /// <summary>文件可视状态。</summary>
    public bool IsFilesSection => ActiveSection == "files";
    /// <summary>转发可视状态。</summary>
    public bool IsForwardSection => ActiveSection == "forward";
    /// <summary>文件区域模型。</summary>
    public SshFilesViewModel Files { get; private set; } = null!;
    /// <summary>当前连接监测。</summary>
    public SshMonitorViewModel Monitor { get; }
    /// <summary>独立于 SQL 助手的 SSH 对话。</summary>
    public SshAssistantViewModel Assistant => ActiveConsole?.Assistant ?? _emptyAssistant;
    /// <summary>显式开关 Agent。</summary>
    public RelayCommand ToggleAgentCommand { get; private set; } = null!;
    /// <summary>显式开关监测详情。</summary>
    public RelayCommand ToggleMonitorCommand { get; private set; } = null!;
    /// <summary>创建本地转发。</summary>
    public AsyncRelayCommand AddForwardCommand { get; private set; } = null!;
    /// <summary>停止选定转发。</summary>
    public RelayCommand StopForwardCommand { get; private set; } = null!;
    /// <summary>活动转发。</summary>
    public ObservableCollection<SshForwardItem> Forwards { get; } = [];
    /// <summary>本机监听端口。</summary>
    public string LocalForwardPort { get; set => SetProperty(ref field, value); } = "8080";
    /// <summary>远端目标主机。</summary>
    public string ForwardHost { get; set => SetProperty(ref field, value); } = "127.0.0.1";
    /// <summary>远端目标端口。</summary>
    public string RemoteForwardPort { get; set => SetProperty(ref field, value); } = "80";
    /// <summary>安全转发错误。</summary>
    public string ForwardError { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>新建转发表单只在实例工具箱中展开。</summary>
    public bool IsAddingForward { get; set => SetProperty(ref field, value); }
    /// <summary>用户可选的规则名称不翻译。</summary>
    public string ForwardName { get; set => SetProperty(ref field, value); } = "";
    /// <summary>打开新建规则，不创建通道。</summary>
    public RelayCommand ShowForwardEditorCommand { get; private set; } = null!;
    /// <summary>取消新建只收起表单。</summary>
    public RelayCommand CancelForwardEditorCommand { get; private set; } = null!;

    private void InitializeWorkspace(IAgentClient? client)
    {
        Files = new(() => IsConnected ? _connection as ISshWorkspaceConnection : null, _dialogs);
        _agentClient = client; _emptyAssistant = new(client, () => null, () => RemoteIdentity, _dialogs);
        InitializeLogs(client);
        InitializeDockerCommands();
        InitializeKubernetes();
        ToggleAgentCommand = new RelayCommand(_ => IsAgentOpen = !IsAgentOpen, _ => HasActiveConsole && !IsClosed);
        ToggleMonitorCommand = new RelayCommand(_ => IsMonitorOpen = !IsMonitorOpen);
        ToggleToolboxCommand = new AsyncRelayCommand(async value =>
        {
            if (value is not string section || section is not ("files" or "forward" or "transfers" or "docker" or "kubernetes") || !SupportsInstanceTools) return;
            ActiveSection = ActiveSection == section ? "terminal" : section;
            if (IsFilesSection && Files.Entries.Count == 0) await Files.RefreshCommand.ExecuteAsync();
            if (IsDockerCommandsOpen) await InitializeDockerPanelAsync();
            if (IsKubernetesOpen) await Kubernetes.InitializeAsync();
        }, _ => !IsClosed && SupportsInstanceTools);
        CollapseToolboxCommand = new RelayCommand(_ => ActiveSection = "terminal");
        AddForwardCommand = new AsyncRelayCommand(_ => AddForwardAsync(), _ => IsConnected && SupportsInstanceTools);
        ShowForwardEditorCommand = new RelayCommand(_ => IsAddingForward = true, _ => IsConnected && SupportsInstanceTools);
        CancelForwardEditorCommand = new RelayCommand(_ => IsAddingForward = false);
        StopForwardCommand = new RelayCommand(value => { if (value is SshForwardItem item) { item.Dispose(); Forwards.Remove(item); } });
    }

    private async Task AddForwardAsync()
    {
        ForwardError = "";
        try
        {
            if (_connection is not ISshWorkspaceConnection connection) throw Text.Exception("SshWorkspaceUnavailable");
            if (!int.TryParse(LocalForwardPort, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int local)
                || !int.TryParse(RemoteForwardPort, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int remote)) throw Text.Exception("SshForwardInvalid");
            var handle = await connection.ForwardAsync(local, ForwardHost, remote, _lifetime.Token);
            if (!IsConnected || !ReferenceEquals(connection, _connection)) { handle.Dispose(); return; }
            Forwards.Add(new(Text.Format("SshForwardEndpoints", local, ForwardHost, remote), handle, local, ForwardHost, remote, ForwardName)); IsAddingForward = false; ForwardName = "";
        }
        catch (Exception error) { ForwardError = Text.Error(error, "SshForwardFailed"); }
    }

    private async Task StopWorkspaceOperationsAsync()
    {
        Files.Cancel(); _emptyAssistant.ConnectionChanged(); foreach (var console in Consoles) console.Assistant.ConnectionChanged(); DisconnectLogs();
        DockerCommands.ConnectionChanged();
        Kubernetes.ConnectionChanged();
        IsDockerResourcesOpen = false; _dockerMonitorStarted = false; await DockerMonitor.StopAsync();
        foreach (var forward in Forwards) forward.Dispose(); Forwards.Clear();
        await Monitor.StopAsync();
    }
}
