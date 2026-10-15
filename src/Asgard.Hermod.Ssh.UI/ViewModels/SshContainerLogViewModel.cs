using Asgard.Hermod.Agent;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>一个容器一份独立日志文档；只使用认证连接的 exec 通道，不共享监测选中状态。</summary>
public sealed class SshContainerLogViewModel : ObservableObject, IDisposable
{
    private ISshWorkspaceConnection? _connection;
    private CancellationTokenSource? _reading;
    private long _revision;
    private bool _disposed;

    /// <summary>容器身份在打开时固定，其他行的选择不会重定向该窗口。</summary>
    public SshContainerLogViewModel(SshMonitorContainerItem container, string host, ISshWorkspaceConnection connection, IAgentClient? client)
    {
        Container = container; Host = host; _connection = connection;
        Assistant = new(client, this);
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !_disposed && !IsLoading && _connection is not null);
        CancelCommand = new RelayCommand(_ => _reading?.Cancel(), _ => IsLoading);
        ToggleAiCommand = new RelayCommand(_ => IsAiOpen = !IsAiOpen);
    }

    /// <summary>用于复用窗口的完整容器 ID。</summary>
    public SshMonitorContainerItem Container { get; }
    /// <summary>公开主机标识，不含连接凭据。</summary>
    public string Host { get; }
    /// <summary>包含容器名称的本地化窗口标题。</summary>
    public string Title => Text.Format("SshMonitorLogTitle", Container.Name);
    /// <summary>日志来源使用完整 ID 区分同名容器。</summary>
    public string Source => Text.Format("SshLogSource", Host, Container.Name, Container.Id);
    /// <summary>协议读取的最近行数，只接受预设数值。</summary>
    public int TailLines { get; set { if (value is 100 or 500 or 1000) SetProperty(ref field, value); } } = 500;
    /// <summary>成功的原始输出，不翻译日志行。</summary>
    public string LogText { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>成功快照时间，UI 使用用户区域格式。</summary>
    public DateTimeOffset? CapturedAt { get; private set; }
    /// <summary>输出限制与请求行数是不同概念，必须向用户明确提示。</summary>
    public bool IsTruncated { get; private set; }
    /// <summary>断线保留快照，但不复用旧连接读取。</summary>
    public bool IsConnected => _connection is not null && !_disposed;
    /// <summary>防止重复读取。</summary>
    public bool IsLoading { get; private set { if (SetProperty(ref field, value)) RefreshCommands(); } }
    /// <summary>安全状态或读取错误，失败时保留上一次快照。</summary>
    public string Status { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>独立的日志分析会话。</summary>
    public SshLogAnalysisViewModel Assistant { get; }
    /// <summary>AI 分屏默认展开，可收起释放阅读空间。</summary>
    public bool IsAiOpen { get; set => SetProperty(ref field, value); } = true;
    /// <summary>本地搜索不拼接远端命令。</summary>
    public string Search { get; set => SetProperty(ref field, value); } = "";
    /// <summary>长行折行只改变阅读外观。</summary>
    public bool WrapLines { get; set => SetProperty(ref field, value); }
    /// <summary>刷新替换当前快照，不清空 AI 对话。</summary>
    public AsyncRelayCommand RefreshCommand { get; }
    /// <summary>取消当前读取。</summary>
    public RelayCommand CancelCommand { get; }
    /// <summary>展开或收起分析。</summary>
    public RelayCommand ToggleAiCommand { get; }

    private async Task RefreshAsync()
    {
        if (_disposed || IsLoading || _connection is not { } connection) return;
        long revision = _revision; int lines = TailLines;
        using var reading = new CancellationTokenSource(TimeSpan.FromSeconds(15)); _reading = reading;
        IsLoading = true; Status = Text.Get("SshMonitorLogLoading");
        try
        {
            var result = await connection.ExecuteCommandAsync(SshMonitorDetailReader.ContainerLogCommand(Container.Id, lines), reading.Token).WaitAsync(reading.Token);
            reading.Token.ThrowIfCancellationRequested();
            if (_disposed || revision != _revision) return;
            if (result.ExitCode != 0) throw Text.Exception("SshMonitorLogFailed");
            Assistant.Selection = "";
            LogText = result.Output; CapturedAt = DateTimeOffset.UtcNow; IsTruncated = result.Truncated;
            OnPropertyChanged(nameof(CapturedAt)); OnPropertyChanged(nameof(IsTruncated));
            Assistant.Refresh();
            Status = Text.Format(result.Truncated ? "SshLogReadTruncated" : "SshLogReadComplete", lines, CapturedAt.Value.ToLocalTime());
            if (result.Output.Length == 0) Status = Text.Get("SshMonitorLogEmpty");
        }
        catch (OperationCanceledException) { if (!_disposed && revision == _revision) Status = Text.Get("SshLogReadCanceled"); }
        catch (Exception error) { if (!_disposed && revision == _revision) Status = Text.Error(error, "SshMonitorLogFailed"); }
        finally { if (ReferenceEquals(_reading, reading)) _reading = null; IsLoading = false; }
    }

    /// <summary>会话结束后取消旧请求，最后成功的快照和本地分析仍可阅读。</summary>
    public void Disconnect()
    {
        _revision++; _connection = null; _reading?.Cancel(); Status = Text.Get("SshLogDisconnected");
        OnPropertyChanged(nameof(IsConnected)); RefreshCommands();
    }
    private void RefreshCommands() { RefreshCommand?.NotifyCanExecuteChanged(); CancelCommand?.NotifyCanExecuteChanged(); }
    /// <summary>剪贴板或本地导出失败不展示系统路径及异常细节。</summary>
    public void ReportLocalError() => Status = Text.Get("SshLogLocalFailed");
    /// <inheritdoc />
    public void Dispose() { if (_disposed) return; _disposed = true; Disconnect(); Assistant.Dispose(); }
}
