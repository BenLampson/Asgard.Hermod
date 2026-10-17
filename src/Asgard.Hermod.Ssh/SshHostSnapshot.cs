namespace Asgard.Hermod.Ssh;

/// <summary>Linux 主机的瞬时采样；首个 CPU 和网络差分读数为空，不能伪造为零。</summary>
public sealed record SshHostSnapshot(string System, double? CpuPercent, long MemoryUsed, long MemoryTotal,
    long DiskUsed, long DiskTotal, double? SentPerSecond, double? ReceivedPerSecond, string Load, string Uptime)
{
    /// <summary>采样 UTC 时间与界面区域格式无关。</summary>
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>逻辑核心数用于将 ps 的进程 CPU 归一到全机尺度。</summary>
    public int ProcessorCount { get; init; } = 1;
    /// <summary>各非回环接口的计数与速率。</summary>
    public IReadOnlyList<SshNetworkSnapshot> Interfaces { get; init; } = [];
}
