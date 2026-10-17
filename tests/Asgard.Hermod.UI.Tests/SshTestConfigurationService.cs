using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.UI.Tests;

/// <summary>会话始终从持久化边界取配置；此替身不修改用户数据库。</summary>
internal sealed class SshTestConfigurationService : IInstanceConfigurationService
{
    internal InstanceConnectionOptions Connection { get; set; } = new()
    {
        TypeId = SshIds.Type,
        ProviderId = SshIds.Direct,
        Parameters = new Dictionary<string, string> { ["host"] = "target.example", ["user"] = "tester", ["password"] = "test-only" },
    };
    public IReadOnlyList<InstanceSummary> GetInstances() => [];
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(Connection);
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
