namespace Asgard.Hermod.UI.Services;

/// <summary>提供 Pencil 中的界面示例；后续由实际应用查询服务替换。</summary>
public sealed class DesignWorkspaceDataSource : IWorkspaceDataSource
{
    private readonly IReadOnlyList<InstanceSummary> _instances =
    [
        new("heimdall", "Heimdall-01", "Heimdall", "server",
            ["Environment/Production/EU West", "Team/Platform/Identity", "Region/Europe/EU West", "Service type/HTTP"],
            Endpoint: "heimdall.prod.asgard.internal:8443"),
        new("bridge", "Bridge-02", "Bridge", "container",
            ["Environment/Production/US East", "Team/Applications/Support", "Region/Americas/US East", "Service type/Docker"]),
        new("tyr", "Tyr-01", "Tyr", "database",
            ["Environment/Staging/Database", "Team/Infrastructure/Database", "Region/Europe/EU West", "Service type/Database"],
            HasAlert: true),
        new("brigitte", "Brigitte-03", "Brigitte", "hexagon",
            ["Environment/Development/Cluster", "Team/Platform/Tools", "Region/Asia/CN East", "Service type/Kubernetes"]),
        new("sandbox", "sandbox-local", "Local terminal", "laptop",
            ["Environment/Development/Local", "Team/Platform/Tools", "Region/Local", "Service type/Local shell"], true),
    ];

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => _instances;

    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => instanceId == "heimdall"
        ? new ServiceSnapshot("Heimdall", "gateway · heimdall.prod.asgard.internal:8443", "Running",
            StatusTone.Success, true,
            [
                new("CPU", "38%", "activity"),
                new("MEMORY", "1.24 / 4 GiB", "database"),
                new("REQUESTS", "2,418 rpm", "arrow-left-right"),
                new("P99 LATENCY", "64 ms", "gauge"),
            ],
            [
                new("image", "asgard/heimdall:2.8.1"),
                new("replicas", "3 / 3"),
                new("region", "cn-east-1"),
                new("log level", "info"),
                new("updated", "2 minutes ago"),
            ],
            [
                new("Deploy #214 finished", "1m", "circle-check", StatusTone.Success),
                new("Health check passed", "2m", "circle-check", StatusTone.Success),
                new("High memory on replica-2", "9m", "triangle-alert", StatusTone.Warning),
                new("Tyr heartbeat lost", "26m", "circle-x", StatusTone.Danger),
                new("Autoscale check skipped", "41m", "circle-dashed", StatusTone.Muted),
            ])
        : null;
}
