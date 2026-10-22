namespace Asgard.Hermod.Ssh;

/// <summary>Docker CLI 的资源快照；速率由同一容器连续采样的累计计数差分，未知值绝不伪装为零。</summary>
public sealed record SshDockerResourceSnapshot(string Id, DateTimeOffset CapturedAt, double? CpuPercent,
    long? MemoryUsed, long? MemoryLimit, double? MemoryPercent, long? ReceivedBytes, long? SentBytes,
    long? ReadBytes, long? WrittenBytes, int? Processes, double? ReceivedPerSecond = null,
    double? SentPerSecond = null, double? ReadPerSecond = null, double? WrittenPerSecond = null);
