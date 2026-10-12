namespace Asgard.Hermod.Core;

/// <summary>使用真实持久化实例作为导航来源，具体实例详情等待独立产品界面接入。</summary>
public sealed class LocalWorkspaceDataSource : IWorkspaceDataSource
{
    /// <summary>只返回安全导航快照的应用服务。</summary>
    private readonly IInstanceConfigurationService _instances;

    /// <summary>数据源不读取 SQLite 或连接参数。</summary>
    public LocalWorkspaceDataSource(IInstanceConfigurationService instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        _instances = instances;
    }

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => _instances.GetInstances();

    /// <inheritdoc />
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
