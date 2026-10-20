namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>容器协议字段与本地化状态分离。</summary>
public sealed record SshMonitorContainerItem(SshContainerSnapshot Value, SshDockerResourceSnapshot? Resources = null)
{
    /// <summary>完整 ID 是日志请求的唯一参数。</summary>
    public string Id => Value.Id;
    /// <summary>用户定义名称。</summary>
    public string Name => Value.Name;
    /// <summary>镜像引用原值。</summary>
    public string Image => Value.Image;
    /// <summary>稳定状态键供过滤。</summary>
    public string State => Value.State;
    /// <summary>运行状态。</summary>
    public bool IsRunning => State == "running";
    /// <summary>健康异常使用文字和颜色共同提示。</summary>
    public bool IsUnhealthy => Value.Health == "unhealthy";
    /// <summary>运行和停止状态的语义颜色。</summary>
    public StatusTone Tone => IsRunning ? StatusTone.Success : StatusTone.Muted;
    /// <summary>健康检查的独立语义颜色。</summary>
    public StatusTone HealthTone => Value.Health == "healthy" ? StatusTone.Success : IsUnhealthy ? StatusTone.Warning : StatusTone.Muted;
    /// <summary>运行状态翻译。</summary>
    public string Status => Text.Get(State switch
    {
        "running" => "SshMonitorRunning",
        "exited" or "dead" => "SshMonitorExited",
        "paused" => "SshMonitorPaused",
        "created" => "SshMonitorCreated",
        "restarting" => "SshMonitorRestarting",
        _ => "SshMonitorNoData",
    });
    /// <summary>健康检查与运行状态单独展示。</summary>
    public string Health => Text.Get(Value.Health switch
    {
        "healthy" => "SshMonitorHealthy",
        "unhealthy" => "SshMonitorUnhealthy",
        "starting" => "SshMonitorStarting",
        _ => "SshMonitorNotConfigured",
    });
    /// <summary>端口协议原值或完整空状态。</summary>
    public string Ports => Value.Ports.Length > 0 ? Value.Ports : Text.Get("SshMonitorNoPorts");
    /// <summary>CPU 原始数值可超过百分之一百，不按主机核心数归一。</summary>
    public double? CpuPercent => IsRunning ? Resources?.CpuPercent : null;
    /// <summary>仅供进度条绑定；文本仍明确区分缺失与零。</summary>
    public double CpuBar => CpuPercent ?? 0;
    /// <summary>网络排序使用接收速率数值。</summary>
    public double? ReceivedPerSecond => IsRunning ? Resources?.ReceivedPerSecond : null;
    /// <summary>磁盘排序使用读取速率数值。</summary>
    public double? ReadPerSecond => IsRunning ? Resources?.ReadPerSecond : null;
    /// <summary>内存排序使用真实字节数。</summary>
    public long? MemoryBytes => IsRunning ? Resources?.MemoryUsed : null;
    /// <summary>内存百分比仅用于可视条；缺失值不冒充零使用量。</summary>
    public double MemoryPercent => IsRunning ? Resources?.MemoryPercent ?? 0 : 0;
    /// <summary>CPU 使用量。</summary>
    public string Cpu => CpuPercent is { } n ? Text.Format("SshMonitorPercent", n) : Text.Get("DockerNoValue");
    /// <summary>内存使用及限制。</summary>
    public string Memory => IsRunning && Resources is { MemoryUsed: { } used, MemoryLimit: { } limit }
        ? Text.Format("DockerMemoryValue", Asgard.Hermod.UI.Localization.FileSizeText.Format(used), Asgard.Hermod.UI.Localization.FileSizeText.Format(limit)) : Text.Get("DockerNoValue");
    /// <summary>接收与发送速率由连续计数差分。</summary>
    public string NetworkRate => Pair(Resources?.ReceivedPerSecond, Resources?.SentPerSecond);
    /// <summary>块设备读写速率由连续计数差分。</summary>
    public string DiskRate => Pair(Resources?.ReadPerSecond, Resources?.WrittenPerSecond);
    /// <summary>进程和内核线程数量。</summary>
    public string Processes => IsRunning && Resources?.Processes is { } n ? n.ToString(System.Globalization.CultureInfo.CurrentCulture) : Text.Get("DockerNoValue");
    private string Pair(double? first, double? second) => IsRunning && first is { } a && second is { } b
        ? Text.Format("DockerRatePair", SshMonitorViewModel.FormatRate(a), SshMonitorViewModel.FormatRate(b)) : Text.Get("DockerNoValue");
}
