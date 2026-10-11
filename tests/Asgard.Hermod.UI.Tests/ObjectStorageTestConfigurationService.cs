namespace Asgard.Hermod.UI.Tests;

/// <summary>按实例标识提供虚构连接，验证资源应用服务无需界面传入凭据。</summary>
public sealed class ObjectStorageTestConfigurationService(InstanceConnectionOptions? options) : IInstanceConfigurationService
{
    /// <summary>保存服务收到的实例标识，便于断言凭据恢复边界。</summary>
    public List<string> LoadedInstanceIds { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => [];
    /// <inheritdoc />
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    /// <inheritdoc />
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LoadedInstanceIds.Add(instanceId);
        return Task.FromResult(options);
    }
}
