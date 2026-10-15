using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>日志文档属于工作区生命周期，每个容器及连接代次复用一个独立窗口。</summary>
public sealed partial class SshWorkspaceViewModel
{
    private readonly List<SshContainerLogViewModel> _logDocuments = [];
    private IAgentClient? _logClient;
    /// <summary>视图接收打开或唤回请求，不通过模态对话阻塞 SSH。</summary>
    public event EventHandler<SshContainerLogViewModel>? LogWindowRequested;

    private void InitializeLogs(IAgentClient? client)
    {
        _logClient = client;
        Monitor.ContainerLogsRequested += OnContainerLogsRequested;
    }

    private void OnContainerLogsRequested(object? sender, SshMonitorContainerItem container)
    {
        if (IsClosed || !IsConnected || _connection is not ISshWorkspaceConnection connection) return;
        var document = _logDocuments.FirstOrDefault(item => item.IsConnected && item.Container.Id == container.Id);
        if (document is null) { document = new(container, RemoteIdentity, connection, _logClient); _logDocuments.Add(document); }
        LogWindowRequested?.Invoke(this, document);
    }

    /// <summary>用户关闭一个日志窗口时只释放该文档，不改变终端或其他日志窗口。</summary>
    public void ReleaseLogDocument(SshContainerLogViewModel document) { _logDocuments.Remove(document); document.Dispose(); }
    private void DisconnectLogs() { foreach (var document in _logDocuments) document.Disconnect(); }
    private void DisposeLogs()
    {
        Monitor.ContainerLogsRequested -= OnContainerLogsRequested;
        foreach (var document in _logDocuments) document.Dispose();
        _logDocuments.Clear(); LogWindowRequested = null;
    }
}
