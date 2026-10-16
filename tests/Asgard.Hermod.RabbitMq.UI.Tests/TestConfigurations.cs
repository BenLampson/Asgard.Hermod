namespace Asgard.Hermod.RabbitMq.UI.Tests;

internal sealed class TestConfigurations : IInstanceConfigurationService, IInstanceEditor
{
    internal static InstanceSummary Instance => new("rabbit", "Production RabbitMQ", "RabbitMQ", "brand-rabbitmq", ["Environment/Production"], TypeId: RabbitMqIds.Type, ProviderId: RabbitMqIds.Direct) { TagIds = ["builtin.environment.production"] };
    public IReadOnlyList<InstanceSummary> GetInstances() => [Instance];
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(FakeRabbitMq.Connection);
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => Task.FromResult(Instance);
    public Task<InstanceSummary?> CreateAsync() => Task.FromResult<InstanceSummary?>(null);
}
