namespace Asgard.Hermod.UI.Tests;

/// <summary>提供可复现的导航快照，特意把本地实例放入前三项。</summary>
internal sealed class TestWorkspaceDataSource : IWorkspaceDataSource
{
    private readonly InstanceSummary[] _instances =
    [
        new("heimdall", "Heimdall-01", "Heimdall", "server", ["Production"]),
        new("sandbox", "sandbox-local", "Local terminal", "laptop", ["Local"], IsLocal: true),
        new("bridge", "Bridge-02", "Bridge", "container", ["Production"]),
        new("tyr", "Tyr-01", "Tyr", "database", ["Staging"], HasAlert: true),
        new("brigitte", "Brigitte-03", "Brigitte", "hexagon", ["Production"]),
    ];

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => _instances;

    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
