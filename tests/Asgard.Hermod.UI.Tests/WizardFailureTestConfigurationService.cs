using Asgard.Hermod.MySql;

namespace Asgard.Hermod.UI.Tests;

/// <summary>提供可编辑实例，验证窗口失败清理时不访问持久化配置或凭据。</summary>
internal sealed class WizardFailureTestConfigurationService : IInstanceConfigurationService
{
    /// <summary>单个测试实例的稳定身份。</summary>
    public IReadOnlyList<InstanceSummary> GetInstances() => [new("failure-test", "MySQL test", "MySQL test", "database", [],
        IsLocal: true, TypeId: MySqlIds.Type, ProviderId: MySqlIds.Direct)];
    /// <summary>窗口故障用例禁止保存。</summary>
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    /// <summary>使用本机占位配置，不包含真实账号或密码。</summary>
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<InstanceConnectionOptions?>(new()
        {
            TypeId = MySqlIds.Type,
            ProviderId = MySqlIds.Direct,
            Parameters = new Dictionary<string, string> { ["host"] = "localhost", ["user"] = "test" },
        });
}
