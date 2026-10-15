using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>每个工作区拥有独立采样生命周期；所有监测命令串行，不使用交互 PTY。</summary>
public sealed partial class SshMonitorViewModel : ObservableObject, IAsyncDisposable
{
    private CancellationTokenSource? _sampling;
    private CancellationTokenSource? _logsCancellation;
    private Task? _task;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ISshWorkspaceConnection? _connection;
    private SshHostMonitor _reader = new();
    private long _generation;
    private long _sectionRevision;
    private long _pauseRevision;

    /// <summary>视图只绑定结构化集合；默认排序使用原始数值。</summary>
    public SshMonitorViewModel()
    {
        ProcessView = CollectionViewSource.GetDefaultView(Processes);
        ProcessView.SortDescriptions.Add(new(nameof(SshMonitorProcessItem.CpuPercent), ListSortDirection.Descending));
        ProcessView.Filter = value => value is SshMonitorProcessItem p && Matches(p.Name, p.User, p.Pid.ToString(CultureInfo.InvariantCulture));
        ContainerView = CollectionViewSource.GetDefaultView(Containers);
        ContainerView.Filter = value => value is SshMonitorContainerItem c && Matches(c.Name, c.Image) && (ContainerState.Length == 0 || c.State == ContainerState);
        MountView = CollectionViewSource.GetDefaultView(Mounts);
        MountView.SortDescriptions.Add(new(nameof(SshMonitorMountItem.UsedPercent), ListSortDirection.Descending));
        MountView.Filter = value => value is SshMonitorMountItem m && Matches(m.Path, m.Device);
        RefreshDetailsCommand = new AsyncRelayCommand(_ => RefreshAsync(true), _ => _connection is not null);
        TogglePauseCommand = new RelayCommand(_ => IsPaused = !IsPaused);
        ReadLogsCommand = new AsyncRelayCommand(value => ReadLogsAsync(value as SshMonitorContainerItem ?? SelectedContainer), value => _connection is not null && IsCurrentContainer(value as SshMonitorContainerItem ?? SelectedContainer));
        ViewContainerDetailsCommand = new RelayCommand(_ => { CloseLogs(); IsContainerDetailsOpen = true; }, _ => IsCurrentContainer(SelectedContainer));
        CloseLogsCommand = new RelayCommand(_ => CloseContainerPane());
        ClearSearchCommand = new RelayCommand(_ => { Search = ""; ContainerState = ""; });
    }

    /// <summary>最近成功采样，失败时保留并显示更新时间与错误状态。</summary>
    public SshHostSnapshot? Snapshot { get; private set; }
    /// <summary>真实两分钟历史；未知值与失败保留断点。</summary>
    public ObservableCollection<SshMonitorSample> History { get; } = [];
    /// <summary>进程使用数值排序和过滤。</summary>
    public ObservableCollection<SshMonitorProcessItem> Processes { get; } = [];
    /// <summary>容器同时包含运行和停止状态。</summary>
    public ObservableCollection<SshMonitorContainerItem> Containers { get; } = [];
    /// <summary>网卡计数与差分来自同一次主机采样。</summary>
    public ObservableCollection<SshMonitorNetworkItem> Networks { get; } = [];
    /// <summary>挂载点容量保留原始字节数。</summary>
    public ObservableCollection<SshMonitorMountItem> Mounts { get; } = [];
    /// <summary>接口选择使用稳定键，全部接口为空键。</summary>
    public ObservableCollection<SshMonitorNetworkChoice> NetworkChoices { get; } = [new("", Text.Get("SshMonitorAllInterfaces"))];
    /// <summary>进程筛选排序视图。</summary>
    public ICollectionView ProcessView { get; }
    /// <summary>容器筛选视图。</summary>
    public ICollectionView ContainerView { get; }
    /// <summary>挂载点筛选排序视图。</summary>
    public ICollectionView MountView { get; }
    /// <summary>主动刷新在暂停时也可读取一次。</summary>
    public AsyncRelayCommand RefreshDetailsCommand { get; }
    /// <summary>暂停不影响连接与终端。</summary>
    public RelayCommand TogglePauseCommand { get; }
    /// <summary>读取容器最近一百行日志。</summary>
    public AsyncRelayCommand ReadLogsCommand { get; }
    /// <summary>收起日志保留筛选和所选行。</summary>
    public RelayCommand CloseLogsCommand { get; }
    /// <summary>清空本地搜索。</summary>
    public RelayCommand ClearSearchCommand { get; }
    /// <summary>安全采样错误。</summary>
    public string Error { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>详情错误独立于主机采样状态。</summary>
    public string SectionError { get; private set => SetProperty(ref field, value); } = "";
    /// <summary>当前页稳定 ID，标题文字不能影响协议行为。</summary>
    public string Section
    {
        get; set
        {
            if (value is not ("overview" or "processes" or "docker" or "network" or "disks") || !SetProperty(ref field, value)) return;
            _sectionRevision++; CloseContainerPane(); Search = ""; SectionError = ""; Notify();
            if (IsPanelVisible) RefreshDetailsCommand.Execute(null);
        }
    } = "overview";
    /// <summary>收起时不自动读取额外详情。</summary>
    public bool IsPanelVisible { get; set { if (SetProperty(ref field, value) && value) RefreshDetailsCommand.Execute(null); } }
    /// <summary>恢复采样重建差分基线，避免跨暂停时长的虚假瞬时值。</summary>
    public bool IsPaused { get; set { if (SetProperty(ref field, value)) { _pauseRevision++; if (!value) _reader = new(); Notify(); } } }
    /// <summary>首次详情读取状态，刷新不遮住已有表格。</summary>
    public bool IsLoading { get; private set => SetProperty(ref field, value); }
    /// <summary>本地搜索不参与 shell。</summary>
    public string Search { get; set { if (SetProperty(ref field, value)) { ProcessView.Refresh(); ContainerView.Refresh(); MountView.Refresh(); ValidateContainerSelection(); Notify(); } } } = "";
    /// <summary>容器状态为协议值。</summary>
    public string ContainerState { get; set { if (SetProperty(ref field, value)) { ContainerView.Refresh(); ValidateContainerSelection(); Notify(); } } } = "";
    /// <summary>接口切换不执行远端命令。</summary>
    public string SelectedNetworkKey { get; set { if (SetProperty(ref field, value)) Notify(); } } = "";
    /// <summary>选中行按 ID 跨刷新恢复。</summary>
    public SshMonitorContainerItem? SelectedContainer
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            if (_logContainer is not null && _logContainer.Id != value?.Id) CloseLogs();
            if (value is null) IsContainerDetailsOpen = false;
            NotifyContainerSelection();
        }
    }
    /// <summary>挂载点选中项用于显示具体容量提醒。</summary>
    public SshMonitorMountItem? SelectedMount { get; set { if (SetProperty(ref field, value)) { OnPropertyChanged(nameof(ShowMountWarning)); OnPropertyChanged(nameof(MountWarning)); } } }
    /// <summary>区分 Docker 不可用与没有容器。</summary>
    public SshContainerAvailability ContainerAvailability { get; private set; } = SshContainerAvailability.Available;

    /// <summary>换连接时清空旧主机数据，迟到结果必须通过代次检查。</summary>
    public async Task StartAsync(ISshWorkspaceConnection? connection)
    {
        await StopAsync(); _connection = connection; _reader = new(); Snapshot = null; History.Clear();
        Processes.Clear(); Containers.Clear(); SelectedContainer = null; Networks.Clear(); Mounts.Clear(); SelectedNetworkKey = "";
        SelectedMount = null; IsLoading = false; _detailsUpdated = null; ContainerAvailability = SshContainerAvailability.Available;
        Reconcile(NetworkChoices, new[] { new SshMonitorNetworkChoice("", Text.Get("SshMonitorAllInterfaces")) }, n => n.Key);
        IsPaused = false; Error = connection is null ? Text.Get("SshMonitorUnavailable") : ""; SectionError = "";
        _loadedSections.Clear(); Notify();
        RefreshDetailsCommand.NotifyCanExecuteChanged(); ReadLogsCommand.NotifyCanExecuteChanged();
        if (connection is null) return;
        _sampling = new(); _task = SampleAsync(_sampling.Token);
    }

    private async Task SampleAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!IsPaused) await RefreshAsync(false);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task RefreshAsync(bool force)
    {
        if (_connection is not { } connection || _sampling is null || !await _gate.WaitAsync(0)) return;
        long generation = _generation, pauseRevision = _pauseRevision;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sampling.Token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            try
            {
                var sample = await _reader.ReadAsync(connection, timeout.Token).WaitAsync(timeout.Token);
                if (generation != _generation || (!force && (IsPaused || pauseRevision != _pauseRevision))) return;
                Snapshot = sample; Error = "";
                History.Add(new(sample.CapturedAt, sample.CpuPercent, sample.MemoryTotal > 0 ? 100d * sample.MemoryUsed / sample.MemoryTotal : null,
                    sample.SentPerSecond, sample.ReceivedPerSecond, sample.Interfaces));
                UpdateNetworks(sample.Interfaces);
            }
            catch (OperationCanceledException) when (generation != _generation || _sampling?.IsCancellationRequested != false) { return; }
            catch (Exception error)
            {
                if (generation != _generation) return;
                Error = Text.Error(error, "SshMonitorUnavailable");
                History.Add(new(DateTimeOffset.UtcNow, null, null, null, null, []));
            }
            while (History.Count > 25 || History.Count > 1 && History[0].Timestamp < DateTimeOffset.UtcNow.AddMinutes(-2)) History.RemoveAt(0);
            if ((force || IsPanelVisible) && (force || !IsPaused) && !timeout.IsCancellationRequested) await ReadSectionAsync(connection, generation, timeout.Token);
        }
        finally { _gate.Release(); Notify(); }
    }

    private bool Matches(params string[] values) => Search.Length == 0 || values.Any(v => v.Contains(Search, StringComparison.OrdinalIgnoreCase));

    /// <summary>断线保留最后成功的数据为只读，下次 Start 才清空。</summary>
    public async Task StopAsync()
    {
        _generation++; _sectionRevision++; _connection = null;
        var sampling = _sampling; var task = _task; _sampling = null; _task = null;
        sampling?.Cancel(); CloseContainerPane();
        if (task is not null) await task;
        sampling?.Dispose(); Error = Text.Get("SshMonitorUnavailable");
        RefreshDetailsCommand.NotifyCanExecuteChanged(); ReadLogsCommand.NotifyCanExecuteChanged(); Notify();
    }
    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync();
}
