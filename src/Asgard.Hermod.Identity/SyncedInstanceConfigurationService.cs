namespace Asgard.Hermod.Identity;

/// <summary>保存范围由当前账户决定，本地离线提交成功后自动排队同步。</summary>
public sealed class SyncedInstanceConfigurationService(IInstanceConfigurationService local, IConfigurationSyncService sync,
    IAccountService accounts) : IInstanceConfigurationService
{
    /// <inheritdoc />
    public IReadOnlyList<InstanceSummary> GetInstances() => local.GetInstances();
    /// <inheritdoc />
    public Task<InstanceConnectionOptions?> GetConnectionAsync(string instanceId, CancellationToken cancellationToken = default) => local.GetConnectionAsync(instanceId, cancellationToken);
    /// <inheritdoc />
    public Task<InstanceSummary> CreateAsync(InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => SaveAsync(null, draft, cancellationToken);
    /// <inheritdoc />
    public Task<InstanceSummary> UpdateAsync(string instanceId, InstanceConfigurationDraft draft, CancellationToken cancellationToken = default) => SaveAsync(instanceId, draft, cancellationToken);
    /// <summary>存储实现只处理当前工作区，不能让调用者指定任意云端账户。</summary>
    private async Task<InstanceSummary> SaveAsync(string? id, InstanceConfigurationDraft draft, CancellationToken cancellationToken)
    {
        InstanceSummary? saved = null;
        await sync.RunLocalChangeAsync(async () =>
        {
            if (draft.CheckExpectedAccount && (draft.ExpectedAccount?.SubjectId != accounts.Account?.SubjectId
                || draft.ExpectedAccount?.TenantId != accounts.Account?.TenantId || draft.ExpectedAccount?.ApplicationId != accounts.Account?.ApplicationId))
                throw Text.Exception("SyncAccountChanged");
            var deviceDraft = new InstanceConfigurationDraft { Name = draft.Name, Connection = draft.Connection, TagIds = draft.TagIds, SaveLocation = InstanceSaveLocation.Local };
            saved = id is null ? await local.CreateAsync(deviceDraft, cancellationToken) : await local.UpdateAsync(id, deviceDraft, cancellationToken);
        }, cancellationToken);
        return saved!;
    }
}
