using Asgard.Hermod.UI.Localization;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>显示模板统一来自共享资源，业务判断只使用固定 ID 和数值。</summary>
public sealed partial class SshMonitorViewModel
{
    /// <summary>当前是否为概览。</summary>
    public bool IsOverview => Section == "overview";
    /// <summary>进程页可视性。</summary>
    public bool IsProcesses => Section == "processes";
    /// <summary>容器页可视性。</summary>
    public bool IsDocker => Section == "docker";
    /// <summary>网络页可视性。</summary>
    public bool IsNetwork => Section == "network";
    /// <summary>磁盘页可视性。</summary>
    public bool IsDisks => Section == "disks";
    /// <summary>当前 CPU 未知时显示等待采样。</summary>
    public string Cpu => Snapshot?.CpuPercent is { } v ? Text.Format("SshMonitorPercent", v) : Text.Get("SshMonitorSampling");
    /// <summary>内存完整容量模板。</summary>
    public string Memory => Snapshot is { } v ? Text.Format("SshMonitorCapacity", v.MemoryUsed / 1073741824d, v.MemoryTotal / 1073741824d) : Text.Get("SshMonitorNoData");
    /// <summary>根分区使用率。</summary>
    public string Disk => Snapshot is { DiskTotal: > 0 } v ? Text.Format("SshMonitorPercent", 100d * v.DiskUsed / v.DiskTotal) : Text.Get("SshMonitorNoData");
    /// <summary>磁盘进度条数值。</summary>
    public double DiskPercent => Snapshot is { DiskTotal: > 0 } v ? Math.Clamp(100d * v.DiskUsed / v.DiskTotal, 0, 100) : 0;
    /// <summary>根分区已用与总容量。</summary>
    public string DiskCapacity => Snapshot is { DiskTotal: > 0 } v ? Text.Format("SshMonitorDiskCapacity", FileSizeText.Format(v.DiskUsed), FileSizeText.Format(v.DiskTotal)) : Text.Get("SshMonitorNoData");
    /// <summary>系统标识为远端原始输出。</summary>
    public string System => Snapshot?.System ?? "";
    /// <summary>CPU 核心数。</summary>
    public string ProcessorCount => Snapshot is { } v ? Text.Format("SshMonitorCores", v.ProcessorCount) : Text.Get("SshMonitorSampling");
    /// <summary>内存使用率及可用空间。</summary>
    public string MemoryHint => Snapshot is { MemoryTotal: > 0 } v ? Text.Format("SshMonitorMemoryHint", 100d * v.MemoryUsed / v.MemoryTotal, FileSizeText.Format(Math.Max(0, v.MemoryTotal - v.MemoryUsed))) : Text.Get("SshMonitorSampling");
    /// <summary>选择的接口或全机汇总。</summary>
    public string NetworkName => SelectedNetworkKey.Length == 0 ? Text.Get("SshMonitorAllInterfaces") : SelectedNetworkKey;
    private SshNetworkSnapshot? SelectedNetwork => Snapshot?.Interfaces.FirstOrDefault(n => n.Name == SelectedNetworkKey);
    /// <summary>上行速率。</summary>
    public string Sent => FormatRate(SelectedNetworkKey.Length == 0 ? Snapshot?.SentPerSecond : SelectedNetwork?.SentPerSecond);
    /// <summary>下行速率。</summary>
    public string Received => FormatRate(SelectedNetworkKey.Length == 0 ? Snapshot?.ReceivedPerSecond : SelectedNetwork?.ReceivedPerSecond);
    /// <summary>完整网络汇总模板。</summary>
    public string Network => Text.Format("SshMonitorTraffic", Sent, Received);
    /// <summary>概览上行不受网络页筛选影响。</summary>
    public string AggregateSent => FormatRate(Snapshot?.SentPerSecond);
    /// <summary>概览下行不受网络页筛选影响。</summary>
    public string AggregateReceived => FormatRate(Snapshot?.ReceivedPerSecond);
    /// <summary>最近成功主机采样时间。</summary>
    public string Updated => Snapshot is { } v ? Text.Format("SshMonitorUpdated", v.CapturedAt.ToLocalTime()) : Text.Get("SshMonitorSampling");
    /// <summary>详情更新时间独立于主机采样时间。</summary>
    public string DetailUpdated => _detailsUpdated is { } v ? Text.Format("SshMonitorUpdated", v.ToLocalTime()) : Text.Get("SshMonitorSampling");
    /// <summary>暂停与恢复的完整动作标签。</summary>
    public string PauseLabel => Text.Get(IsPaused ? "SshMonitorResume" : "SshMonitorPause");
    /// <summary>错误或暂停提示不遮挡已有数据。</summary>
    public bool HasBanner => Error.Length > 0 || SectionError.Length > 0 || IsPaused;
    /// <summary>优先展示当前页的安全错误。</summary>
    public string BannerText => SectionError.Length > 0 ? SectionError : Error.Length > 0 ? Error : Text.Get("SshMonitorPausedHint");
    /// <summary>当前详情首次加载时才显示加载提示。</summary>
    public bool ShowLoading => IsLoading && !_loadedSections.Contains(Section);
    /// <summary>过滤后表格没有内容时展示独立空状态。</summary>
    public bool ShowEmpty => !IsLoading && (_loadedSections.Contains(Section) || SectionError.Length > 0) && Section switch
    {
        "processes" => ProcessView.IsEmpty,
        "docker" => ContainerView.IsEmpty,
        "disks" => MountView.IsEmpty,
        _ => false,
    };
    private string ContainerErrorKey => ContainerAvailability switch
    {
        SshContainerAvailability.NotInstalled => "SshMonitorDockerMissing",
        SshContainerAvailability.AccessDenied => "SshMonitorDockerDenied",
        _ => "SshMonitorDetailUnavailable",
    };
    /// <summary>空状态标题区分无安装、权限、空列表与搜索无匹配。</summary>
    public string EmptyTitle => Text.Get(Search.Length > 0 || IsDocker && ContainerState.Length > 0 ? "SshMonitorNoMatches" : IsDocker && ContainerAvailability != SshContainerAvailability.Available ? ContainerErrorKey : "SshMonitorEmpty");
    /// <summary>空状态说明使用完整模板。</summary>
    public string EmptyDescription => SectionError.Length > 0 ? SectionError : Text.Get(Search.Length > 0 || IsDocker && ContainerState.Length > 0 ? "SshMonitorNoMatchesHint" : IsDocker ? "SshMonitorDockerEmptyHint" : "SshMonitorEmptyHint");
    /// <summary>进程总数。</summary>
    public int ProcessCount => Processes.Count;
    /// <summary>运行中进程数。</summary>
    public int RunningProcesses => Processes.Count(p => p.Value.State.StartsWith('R'));
    /// <summary>容器总数。</summary>
    public int ContainerCount => Containers.Count;
    /// <summary>运行中容器。</summary>
    public int RunningContainers => Containers.Count(c => c.IsRunning);
    /// <summary>已停止容器。</summary>
    public int StoppedContainers => Containers.Count(c => c.State is "exited" or "dead");
    /// <summary>健康异常容器。</summary>
    public int UnhealthyContainers => Containers.Count(c => c.IsUnhealthy);
    /// <summary>去重后的持久文件系统数。</summary>
    public int PersistentCount => Mounts.Where(m => m.Value.IsPersistent).DistinctBy(m => m.Device).Count();
    /// <summary>持久文件系统可用容量。</summary>
    public string PersistentAvailable => FileSizeText.Format(Mounts.Where(m => m.Value.IsPersistent).DistinctBy(m => m.Device).Sum(m => m.AvailableBytes));
    /// <summary>高使用率文件系统数。</summary>
    public int HighUsageCount => Mounts.Where(m => m.Value.IsPersistent && m.IsHighUsage).DistinctBy(m => m.Device).Count();
    /// <summary>高容量分区需要显示具体路径与可用量。</summary>
    public bool ShowMountWarning => SelectedMount?.IsHighUsage == true;
    /// <summary>完整容量提示模板。</summary>
    public string MountWarning => SelectedMount is { } m ? Text.Format("SshMonitorMountWarning", m.Path, m.UsedPercent, m.Available) : "";
    /// <summary>根分区表格只显示当前主机真实挂载项。</summary>
    public IReadOnlyList<SshMonitorMountItem> RootMounts => Mounts.Where(m => m.Path == "/").ToArray();
    /// <summary>本地过滤后与总记录数。</summary>
    public string ResultCount => Text.Format("SshMonitorResultCount", IsProcesses ? ProcessView.Cast<object>().Count() : IsDocker ? ContainerView.Cast<object>().Count() : MountView.Cast<object>().Count(),
        IsProcesses ? Processes.Count : IsDocker ? Containers.Count : Mounts.Count);
    /// <summary>读取达到上限时明确告知。</summary>
    public bool ReachedLimit => (IsProcesses ? Processes.Count : IsDocker ? Containers.Count : Mounts.Count) >= SshMonitorDetailReader.MaximumItems;
    /// <summary>CPU 曲线保留未知点。</summary>
    public IReadOnlyList<SshMonitorPoint> CpuPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, s.Cpu)).ToArray();
    /// <summary>内存固定百分比曲线。</summary>
    public IReadOnlyList<SshMonitorPoint> MemoryPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, s.Memory)).ToArray();
    /// <summary>选中接口或全机上行。</summary>
    public IReadOnlyList<SshMonitorPoint> SentPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, SelectedNetworkKey.Length == 0 ? s.Sent : s.Interfaces.FirstOrDefault(n => n.Name == SelectedNetworkKey)?.SentPerSecond)).ToArray();
    /// <summary>选中接口或全机下行。</summary>
    public IReadOnlyList<SshMonitorPoint> ReceivedPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, SelectedNetworkKey.Length == 0 ? s.Received : s.Interfaces.FirstOrDefault(n => n.Name == SelectedNetworkKey)?.ReceivedPerSecond)).ToArray();
    /// <summary>概览始终显示全机汇总，不受网络页接口筛选影响。</summary>
    public IReadOnlyList<SshMonitorPoint> AggregateSentPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, s.Sent)).ToArray();
    /// <summary>全机下行曲线。</summary>
    public IReadOnlyList<SshMonitorPoint> AggregateReceivedPoints => History.Select(s => new SshMonitorPoint(s.Timestamp, s.Received)).ToArray();

    /// <summary>区域格式与界面语言分离，未知值明确等待采样。</summary>
    public static string FormatRate(double? value) => value is { } number && double.IsFinite(number) && number >= 0
        ? Text.Format("SshMonitorRate", FileSizeText.Format(number >= long.MaxValue ? long.MaxValue : (long)number)) : Text.Get("SshMonitorSampling");

    private void Notify()
    {
        foreach (string name in new[] { nameof(Snapshot), nameof(Cpu), nameof(Memory), nameof(Disk), nameof(DiskPercent), nameof(DiskCapacity), nameof(System),
            nameof(ProcessorCount), nameof(MemoryHint), nameof(NetworkName), nameof(Sent), nameof(Received), nameof(Network), nameof(Updated), nameof(DetailUpdated),
            nameof(PauseLabel), nameof(HasBanner), nameof(BannerText), nameof(ShowLoading), nameof(ShowEmpty), nameof(EmptyTitle), nameof(EmptyDescription),
            nameof(IsOverview), nameof(IsProcesses), nameof(IsDocker), nameof(IsNetwork), nameof(IsDisks), nameof(ProcessCount), nameof(RunningProcesses),
            nameof(ContainerCount), nameof(RunningContainers), nameof(StoppedContainers), nameof(UnhealthyContainers), nameof(PersistentCount), nameof(PersistentAvailable),
            nameof(HighUsageCount), nameof(RootMounts), nameof(ResultCount), nameof(ReachedLimit), nameof(CpuPoints), nameof(MemoryPoints), nameof(SentPoints), nameof(ReceivedPoints),
            nameof(AggregateSentPoints), nameof(AggregateReceivedPoints), nameof(AggregateSent), nameof(AggregateReceived), nameof(ShowMountWarning), nameof(MountWarning) }) OnPropertyChanged(name);
        NotifyContainerSelection();
    }
}
