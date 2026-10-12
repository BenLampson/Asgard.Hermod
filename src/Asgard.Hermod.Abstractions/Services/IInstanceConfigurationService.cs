namespace Asgard.Hermod.Abstractions.Services;

/// <summary>管理实例配置，界面不直接接触数据库或凭据保护实现。</summary>
public interface IInstanceConfigurationService
{
    /// <summary>读取不包含凭据的导航快照。</summary>
    IReadOnlyList<InstanceSummary> GetInstances();
    /// <summary>保存配置与标签关联，成功后返回安全的导航快照。</summary>
    Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default);
    /// <summary>更新已保存配置，保留实例标识并返回不包含凭据的最新导航信息。</summary>
    Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("当前配置服务不支持编辑实例。");
    /// <summary>仅向需要建立连接的应用服务返回解密配置。</summary>
    Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId,
        CancellationToken cancellationToken = default);
}
