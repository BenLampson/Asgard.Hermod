namespace Asgard.Hermod.Redis.Tests;

internal sealed class RedisTestConfigurations : IInstanceConfigurationService, IInstanceEditor, IWorkspaceDataSource
{
    internal List<InstanceConfigurationDraft> Drafts { get; } = [];
    internal static InstanceSummary Instance { get; } = new("redis-test", "Production · Cache", "Production · Cache", "database", [],
        TypeId: RedisIds.Type, ProviderId: RedisIds.Standalone);
    public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default)
    { Drafts.Add(draft); return Task.FromResult(Instance); }
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(RedisTestSession.Connection);
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
