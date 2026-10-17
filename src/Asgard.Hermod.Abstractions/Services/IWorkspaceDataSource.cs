namespace Asgard.Hermod.Abstractions.Services;

/// <summary>由应用层向界面提供导航与详情快照。</summary>
public interface IWorkspaceDataSource
{
    /// <summary>读取可展示的实例。</summary>
    IReadOnlyList<InstanceSummary> GetInstances();
    /// <summary>读取详情；未配置时返回 null。</summary>
    ServiceSnapshot? GetServiceSnapshot(string instanceId);
}
