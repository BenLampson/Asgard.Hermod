namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>容器协议字段与本地化状态分离。</summary>
public sealed record SshMonitorContainerItem(SshContainerSnapshot Value)
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
}
