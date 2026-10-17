namespace Asgard.Hermod.UI.Tests;

/// <summary>用多级标签、无标签和相似前缀验证真实导航过滤。</summary>
internal sealed class TaggedWorkspaceDataSource : IWorkspaceDataSource
{
    private readonly InstanceSummary[] _instances =
    [
        new("api-east", "API East", "Gateway", "server",
            ["Environment/Production/cn-east", "Owner/Platform"]),
        new("api-west", "API West", "Gateway", "server",
            ["Environment/Production/eu-west", "Owner/Payments"]),
        new("worker", "Worker", "Background", "container",
            ["Environment/Staging", "Owner/Platform"]),
        new("copy", "Copy", "Sandbox", "laptop",
            ["Environment/Production-copy", "Owner/Platform"]),
        new("root", "Production root", "Root", "server", ["Environment/Production"]),
        new("untagged", "Untagged", "Utility", "hexagon", []),
    ];

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => _instances;

    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
