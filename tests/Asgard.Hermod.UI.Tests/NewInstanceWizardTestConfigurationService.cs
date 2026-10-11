namespace Asgard.Hermod.UI.Tests;

/// <summary>可控的配置保存替身，不接触文件或数据库，用于验证提交和恢复行为。</summary>
internal sealed class NewInstanceWizardTestConfigurationService : IInstanceConfigurationService
{
    private readonly List<InstanceConfigurationDraft> _drafts = [];
    private readonly List<(string InstanceId, InstanceConfigurationDraft Draft)> _updates = [];

    /// <summary>记录保存调用，便于验证重复提交不会穿透服务边界。</summary>
    public IReadOnlyList<InstanceConfigurationDraft> Drafts => _drafts;
    /// <summary>保存次数。</summary>
    public int CallCount => _drafts.Count;
    /// <summary>更新必须携带原实例标识，不应进入创建接口。</summary>
    public IReadOnlyList<(string InstanceId, InstanceConfigurationDraft Draft)> Updates => _updates;
    /// <summary>编辑模式的提交次数。</summary>
    public int UpdateCallCount => _updates.Count;
    /// <summary>保存期间收到的取消令牌。</summary>
    public CancellationToken LastCancellationToken { get; private set; }
    /// <summary>测试可控制保存失败或完成时机。</summary>
    public Func<InstanceConfigurationDraft, CancellationToken, Task<InstanceSummary>> CreateHandler { get; set; } =
        (draft, _) => Task.FromResult(new InstanceSummary("persisted-instance", draft.Name, draft.Name, "cloud", []));
    /// <summary>测试可控制现有实例的更新结果。</summary>
    public Func<string, InstanceConfigurationDraft, CancellationToken, Task<InstanceSummary>> UpdateHandler { get; set; } =
        (id, draft, _) => Task.FromResult(new InstanceSummary(id, draft.Name, draft.Name, "cloud", [],
            IsLocal: true, TypeId: draft.Connection.TypeId, ProviderId: draft.Connection.ProviderId));

    /// <summary>替身只返回安全快照，不读取持久化数据。</summary>
    public IReadOnlyList<InstanceSummary> GetInstances() => [];

    /// <summary>记录完整提交后执行指定的保存结果。</summary>
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        _drafts.Add(draft);
        LastCancellationToken = cancellationToken;
        return CreateHandler(draft, cancellationToken);
    }

    /// <summary>分别记录编辑事务，避免重复更新或把编辑误提交为创建。</summary>
    public Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft,
        CancellationToken cancellationToken = default)
    {
        _updates.Add((instanceId, draft));
        LastCancellationToken = cancellationToken;
        return UpdateHandler(instanceId, draft, cancellationToken);
    }

    /// <summary>创建向导不需要读取其他实例的连接信息。</summary>
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId,
        CancellationToken cancellationToken = default) => Task.FromResult<InstanceConnectionOptions?>(null);
}
