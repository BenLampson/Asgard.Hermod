namespace Asgard.Hermod.Abstractions.Services;

/// <summary>管理实例配置，界面不直接接触数据库或凭据保护实现。</summary>
public interface IInstanceConfigurationService
{
    /// <summary>删除本工作区配置和标签关联，不删除远程资源、不关闭已有会话。</summary>
    Task DeleteAsync(string instanceId, InstanceDeleteRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
    /// <summary>读取不包含凭据的导航快照。</summary>
    IReadOnlyList<InstanceSummary> GetInstances();
    /// <summary>仅更新名称或标签，不读取、测试或改写连接参数和凭据。</summary>
    Task<InstanceSummary> UpdateMetadataAsync(string instanceId, InstanceMetadataChange change,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
    /// <summary>读取实例关联的稳定标签标识，编辑器不从显示路径反推身份。</summary>
    Task<IReadOnlyList<string>> GetTagIdsAsync(string instanceId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
    /// <summary>保存配置与标签关联，成功后返回安全的导航快照。</summary>
    Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default);
    /// <summary>更新已保存配置，保留实例标识并返回不包含凭据的最新导航信息。</summary>
    Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheCurrentConfigurationServiceDoesNotSupportEditingInstances"));
    /// <summary>仅向需要建立连接的应用服务返回解密配置。</summary>
    Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId,
        CancellationToken cancellationToken = default);
}
