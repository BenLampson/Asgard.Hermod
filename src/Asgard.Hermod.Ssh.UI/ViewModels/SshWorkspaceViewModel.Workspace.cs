using System.Collections.ObjectModel;
using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>工作区的文件、监测、转发和助手只共享连接，不共享交互 PTY 的字节流。</summary>
public sealed partial class SshWorkspaceViewModel
{
    /// <summary>默认收起的 Agent，显隐不影响实例状态。</summary>
    public bool IsAgentOpen { get; set => SetProperty(ref field, value); }
    /// <summary>从标题栏显式打开监测，收起时不占终端高度。</summary>
    public bool IsMonitorOpen { get; set => SetProperty(ref field, value); }
    /// <summary>功能区的稳定 ID。</summary>
    public string ActiveSection { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(IsTerminalSection)); OnPropertyChanged(nameof(IsFilesSection)); OnPropertyChanged(nameof(IsForwardSection)); } } } = "terminal";
    /// <summary>终端可视状态。</summary>
    public bool IsTerminalSection => ActiveSection == "terminal";
    /// <summary>文件可视状态。</summary>
    public bool IsFilesSection => ActiveSection == "files";
    /// <summary>转发可视状态。</summary>
    public bool IsForwardSection => ActiveSection == "forward";
    /// <summary>文件区域模型。</summary>
    public SshFilesViewModel Files { get; private set; } = null!;
    /// <summary>当前连接监测。</summary>
    public SshMonitorViewModel Monitor { get; } = new();
    /// <summary>独立于 SQL 助手的 SSH 对话。</summary>
    public SshAssistantViewModel Assistant { get; private set; } = null!;
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

    private void InitializeWorkspace(IAgentClient? client)
    {
        Files = new(() => IsConnected ? _connection as ISshWorkspaceConnection : null, _dialogs);
        Assistant = new(client, () => IsConnected ? _connection as ISshWorkspaceConnection : null, () => RemoteIdentity, _dialogs);
        ToggleAgentCommand = new RelayCommand(_ => IsAgentOpen = !IsAgentOpen);
        ToggleMonitorCommand = new RelayCommand(_ => IsMonitorOpen = !IsMonitorOpen);
        AddForwardCommand = new AsyncRelayCommand(_ => AddForwardAsync(), _ => IsConnected);
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
            Forwards.Add(new(Text.Format("SshForwardEndpoints", local, ForwardHost, remote), handle));
        }
        catch (Exception error) { ForwardError = Text.Error(error, "SshForwardFailed"); }
    }

    private async Task StopWorkspaceOperationsAsync()
    {
        Files.Cancel(); Assistant.ConnectionChanged();
        foreach (var forward in Forwards) forward.Dispose(); Forwards.Clear();
        await Monitor.StopAsync();
    }
}
