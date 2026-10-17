namespace Asgard.Hermod.Redis.Tests;

internal sealed class RedisTestConfigurations : IInstanceConfigurationService, IInstanceEditor, IWorkspaceDataSource
{
    internal List<InstanceConfigurationDraft> Drafts { get; } = [];
    internal static InstanceSummary Instance { get; } = new("redis-test", "Production · Cache", "Production · Cache", "database", [],
        TypeId: RedisIds.Type, ProviderId: RedisIds.Standalone);
    internal InstanceSummary CurrentInstance { get; private set; } = Instance;
    internal int Renames { get; private set; }
    public Task<InstanceSummary?> RenameAsync(string instanceId)
    { Renames++; CurrentInstance = CurrentInstance with { Name = "Renamed cache", DisplayName = "Renamed cache" }; return Task.FromResult<InstanceSummary?>(CurrentInstance); }
    public IReadOnlyList<InstanceSummary> GetInstances() => [CurrentInstance];
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default)
    { Drafts.Add(draft); return Task.FromResult(Instance); }
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(RedisTestSession.Connection);
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
    public ServiceSnapshot? GetServiceSnapshot(string instanceId) => null;
}
