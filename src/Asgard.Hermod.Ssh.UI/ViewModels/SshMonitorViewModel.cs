using System.Collections.ObjectModel;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>每条连接拥有一个不重叠的五秒采样循环；失败展示不可用而不是模拟读数。</summary>
public sealed class SshMonitorViewModel : ObservableObject, IAsyncDisposable
{
    private CancellationTokenSource? _sampling;
    private Task? _task;
    private ISshWorkspaceConnection? _connection;
    private bool _readingDetails;
    private long _revision;

    /// <summary>创建采样与详情命令。</summary>
    public SshMonitorViewModel()
    {
        RefreshDetailsCommand = new AsyncRelayCommand(_ => RefreshDetailsAsync(), _ => !_readingDetails && !IsOverview);
    }

    /// <summary>当前真实采样。</summary>
    public SshHostSnapshot? Snapshot { get; private set; }
    /// <summary>最近二十四次真实采样，不使用演示数据填充。</summary>
    public ObservableCollection<SshMonitorSample> History { get; } = [];
    /// <summary>顶部紧凑摘要。</summary>
    public string Summary => Snapshot is null ? (Error.Length > 0 ? Error : Text.Get("SshMonitorSampling")) : Text.Format("SshMonitorSummary", Cpu, Memory, Disk, Network);
    /// <summary>CPU 差分百分比。</summary>
    public string Cpu => Snapshot?.CpuPercent is { } value ? Text.Format("SshMonitorPercent", value) : MissingValue;
    /// <summary>内存容量。</summary>
    public string Memory => Snapshot is { } value ? Text.Format("SshMonitorCapacity", value.MemoryUsed / 1073741824d, value.MemoryTotal / 1073741824d) : MissingValue;
    /// <summary>根磁盘容量。</summary>
    public string Disk => Snapshot is { DiskTotal: > 0 } value ? Text.Format("SshMonitorPercent", 100d * value.DiskUsed / value.DiskTotal) : MissingValue;
    /// <summary>网络差分速率。</summary>
    public string Network => Snapshot is { SentPerSecond: { } sent, ReceivedPerSecond: { } received } ? Text.Format("SshMonitorNetworkRate", sent / 1024, received / 1024) : MissingValue;
    private string MissingValue => Text.Get(Error.Length > 0 ? "SshMonitorNoData" : "SshMonitorSampling");
    /// <summary>原始系统标识。</summary>
    public string System => Snapshot?.System ?? "";
    /// <summary>安全错误。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>详情固定分区 ID。</summary>
    public string Section { get; set { if (SetProperty(ref field, value)) { _revision++; Details = ""; OnPropertyChanged(nameof(IsOverview)); RefreshDetailsCommand.NotifyCanExecuteChanged(); } } } = "overview";
    /// <summary>默认概览不执行额外诊断。</summary>
    public bool IsOverview => Section == "overview";
    /// <summary>用户按需读取的原始只读诊断输出。</summary>
    public string Details { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>刷新当前详情页。</summary>
    public AsyncRelayCommand RefreshDetailsCommand { get; }

    /// <summary>只在连接成功后启动；重连先停止旧循环。</summary>
    public async Task StartAsync(ISshWorkspaceConnection? connection)
    {
        await StopAsync(); _connection = connection; Snapshot = null;
        if (connection is null) { Error = Text.Get("SshMonitorUnavailable"); Notify(); return; }
        Error = ""; Notify(); _sampling = new(); _task = SampleAsync(connection, _sampling.Token);
    }

    private async Task SampleAsync(ISshWorkspaceConnection connection, CancellationToken token)
    {
        var monitor = new SshHostMonitor();
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(8));
                    var sample = await monitor.ReadAsync(connection, timeout.Token);
                    token.ThrowIfCancellationRequested(); Snapshot = sample; Error = "";
                    History.Add(new(Math.Clamp(sample.CpuPercent ?? 0, 0, 100) * 0.6,
                        sample.MemoryTotal > 0 ? Math.Clamp(100d * sample.MemoryUsed / sample.MemoryTotal, 0, 100) * 0.6 : 0));
                    while (History.Count > 24) History.RemoveAt(0);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (Exception error) { Snapshot = null; Error = Text.Error(error, "SshMonitorUnavailable"); }
                Notify(); await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshDetailsAsync()
    {
        if (_connection is null || _sampling is null || _readingDetails || IsOverview) return;
        long revision = _revision; _readingDetails = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sampling.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var result = await _connection.ExecuteCommandAsync(SshHostMonitor.DetailCommand(Section), timeout.Token);
            if (revision == _revision) Details = result.ExitCode == 0 && !result.Truncated ? result.Output : Text.Get("SshMonitorUnavailable");
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (revision == _revision) Details = Text.Error(error, "SshMonitorUnavailable"); }
        finally { _readingDetails = false; }
    }

    private void Notify()
    {
        foreach (string name in new[] { nameof(Summary), nameof(Cpu), nameof(Memory), nameof(Disk), nameof(Network), nameof(System), nameof(Snapshot) }) OnPropertyChanged(name);
    }

    /// <summary>断线后清空过期读数，终止采样及详情请求。</summary>
    public async Task StopAsync()
    {
        var sampling = _sampling; var task = _task;
        _sampling = null; _task = null; _connection = null;
        sampling?.Cancel();
        Snapshot = null; Details = ""; History.Clear(); _revision++; Error = Text.Get("SshMonitorUnavailable"); Notify();
        if (task is not null) await task;
        sampling?.Dispose();
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync();
}
