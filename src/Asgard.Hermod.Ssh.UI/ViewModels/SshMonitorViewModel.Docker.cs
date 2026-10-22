using System.Diagnostics;

namespace Asgard.Hermod.Ssh.UI.ViewModels;

/// <summary>Docker 资源采样和两分钟历史与终端独立，生命周期受同一连接代次约束。</summary>
public sealed partial class SshMonitorViewModel
{
    private readonly SshDockerStatsReader _dockerStats = new();
    private readonly Dictionary<string, List<SshDockerResourceSnapshot>> _containerHistory = [];
    private string? _requestedContainerId;
    /// <summary>从管理页返回监测时保留目标，即使首次异步读取尚未完成。</summary>
    public void SelectDockerContainer(string id)
    {
        _ = SshMonitorDetailReader.ContainerLogCommand(id);
        Section = "docker"; Search = ""; ContainerState = "";
        SelectedContainer = Containers.FirstOrDefault(c => c.Id == id);
        _requestedContainerId = id;
    }
    /// <summary>命令入口由工作区打开，不在监测中执行任意 shell。</summary>
    public event EventHandler<string>? DockerOperationRequested;
    /// <summary>从监测带入容器。</summary>
    public RelayCommand OpenDockerCommandsCommand { get; }
    /// <summary>已停止或新建的容器直接进入启动确认。</summary>
    public RelayCommand StartContainerCommand { get; }
    /// <summary>打开带重启参数的确认流程。</summary>
    public RelayCommand RestartContainerCommand { get; }
    /// <summary>运行中的容器可进入停止流程。</summary>
    public RelayCommand StopContainerCommand { get; }
    /// <summary>当前连接的 Docker 能力。</summary>
    public SshDockerEnvironment? DockerEnvironment { get; private set; }
    /// <summary>引擎与 Compose 环境摘要。</summary>
    public string DockerEnvironmentSummary => DockerEnvironment is { Installed: true } e ? Text.Format("DockerMonitorEnvironment", e.EngineVersion,
        e.ComposeVersion.Length > 0 ? e.ComposeVersion : Text.Get("DockerComposeUnavailable")) : Text.Get("DockerDetectFirst");
    /// <summary>选中项才有趋势区域。</summary>
    public bool HasSelectedContainer => SelectedContainer is not null;
    /// <summary>完整趋势标题。</summary>
    public string ContainerTrendTitle => Text.Format("DockerTrendTitle", SelectedContainer?.Name ?? "");
    /// <summary>当前 CPU 数值。</summary>
    public string ContainerCpu => SelectedContainer?.Cpu ?? Text.Get("DockerNoValue");
    /// <summary>当前内存用量及上限。</summary>
    public string ContainerMemory => SelectedContainer?.Memory ?? Text.Get("DockerNoValue");
    /// <summary>CPU 图以逻辑核心数量设上限，保留超过百分之一百的真实值。</summary>
    public double ContainerCpuMaximum => Math.Max(100, Math.Max((Snapshot?.ProcessorCount ?? 1) * 100, SelectedContainer?.CpuPercent ?? 0));
    /// <summary>真实时间 CPU 曲线；丢失的采样保留断点。</summary>
    public IReadOnlyList<SshMonitorPoint> ContainerCpuPoints => SelectedHistory.Select(s => new SshMonitorPoint(s.CapturedAt, s.CpuPercent)).ToArray();
    /// <summary>内存使用百分比曲线。</summary>
    public IReadOnlyList<SshMonitorPoint> ContainerMemoryPoints => SelectedHistory.Select(s => new SshMonitorPoint(s.CapturedAt, s.MemoryPercent)).ToArray();
    private IEnumerable<SshDockerResourceSnapshot> SelectedHistory => SelectedContainer is { } c && _containerHistory.TryGetValue(c.Id, out var history) ? history : [];

    private async Task<Dictionary<string, SshDockerResourceSnapshot>> ReadContainerResourcesAsync(ISshWorkspaceConnection connection,
        IReadOnlyList<SshContainerSnapshot> containers, long generation, long revision, CancellationToken token)
    {
        var resources = new Dictionary<string, SshDockerResourceSnapshot>();
        try
        {
            var environment = await _dockerEnvironmentReader.ReadAsync(connection, false, token);
            if (generation != _generation || revision != _sectionRevision) return resources;
            DockerEnvironment = environment;
            if (!environment.SupportsStats) throw Text.Exception("DockerStatsUnsupported");
            var running = containers.Where(c => c.State == "running").Select(c => c.Id).ToArray();
            if (running.Length > 0)
            {
                var result = await connection.ExecuteCommandAsync(SshDockerStatsReader.Command(running), token).WaitAsync(token);
                if (generation != _generation || revision != _sectionRevision) return resources;
                if (result.ExitCode != 0 || result.Truncated) throw Text.Exception("DockerStatsUnavailable");
                var expected = running.ToHashSet(StringComparer.Ordinal);
                var samples = _dockerStats.Parse(result.Output, Stopwatch.GetTimestamp(), DateTimeOffset.UtcNow);
                if (samples.Any(s => !expected.Contains(s.Id))) throw Text.Exception("DockerStatsUnavailable");
                resources = samples.ToDictionary(s => s.Id, StringComparer.Ordinal);
            }
            else _dockerStats.Reset();
            SectionError = "";
        }
        catch (Exception error) when (generation == _generation && revision == _sectionRevision)
        {
            _dockerStats.Reset(); SectionError = Text.Error(error, "DockerStatsUnavailable");
            // 保留最后成功数值并明确显示错误；历史中追加空点，不能把旧值绘制为新采样。
            resources = Containers.Where(c => c.Resources is not null && containers.Any(n => n.Id == c.Id && n.State == "running"))
                .ToDictionary(c => c.Id, c => c.Resources!, StringComparer.Ordinal);
        }
        if (generation != _generation || revision != _sectionRevision) return resources;
        var ids = containers.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (string removed in _containerHistory.Keys.Where(id => !ids.Contains(id)).ToArray()) _containerHistory.Remove(removed);
        foreach (var container in containers)
        {
            if (!_containerHistory.TryGetValue(container.Id, out var history)) _containerHistory[container.Id] = history = [];
            var sample = SectionError.Length == 0 && resources.TryGetValue(container.Id, out var value) ? value
                : new SshDockerResourceSnapshot(container.Id, DateTimeOffset.UtcNow, null, null, null, null, null, null, null, null, null);
            history.Add(sample);
            while (history.Count > 25 || history.Count > 1 && history[0].CapturedAt < DateTimeOffset.UtcNow.AddMinutes(-2)) history.RemoveAt(0);
        }
        NotifyDockerResources(); return resources;
    }

    private void NotifyDockerResources()
    {
        foreach (string property in new[] { nameof(DockerEnvironmentSummary), nameof(HasSelectedContainer), nameof(ContainerTrendTitle), nameof(ContainerCpu),
            nameof(ContainerMemory), nameof(ContainerCpuMaximum), nameof(ContainerCpuPoints), nameof(ContainerMemoryPoints) }) OnPropertyChanged(property);
    }
}
