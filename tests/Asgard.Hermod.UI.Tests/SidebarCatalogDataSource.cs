namespace Asgard.Hermod.UI.Tests;

/// <summary>以稳定标签标识生成最新路径，模拟实际持久化实例的安全快照。</summary>
internal sealed class SidebarCatalogDataSource(SidebarTestTagService tags) : IWorkspaceDataSource
{
    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() =>
    [
        new("oss", "Storage", "Storage", "cloud", tags.Tags.Where(tag => tag.Id == "platform").Select(tag => tag.Path).ToArray()),
    ];
    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
