namespace Asgard.Hermod.Identity;

/// <summary>按实例比较云端共同基准，只向界面公开状态，不公开参数或同步密码。</summary>
public sealed partial class ConfigurationSyncService
{
    private volatile IReadOnlySet<string> _syncedInstanceKeys = new HashSet<string>(StringComparer.Ordinal);

    /// <inheritdoc />
    public ConfigurationSyncStatus GetInstanceStatus(string instanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        var account = _accounts.Account;
        if (account is null) return ConfigurationSyncStatus.Local;
        if (_syncedInstanceKeys.Contains(Owner(account) + ":" + instanceId)) return ConfigurationSyncStatus.Synced;
        return Status switch
        {
            ConfigurationSyncStatus.Local => ConfigurationSyncStatus.PasswordRequired,
            ConfigurationSyncStatus.Synced => ConfigurationSyncStatus.Pending,
            _ => Status,
        };
    }

    /// <summary>显示状态计算失败不得把已提交的本机保存误报成失败；清空缓存保持保守的待同步标记。</summary>
    private async Task RefreshCloudInstanceStateAsync(HermodAccount? account, WorkspaceSnapshot? baseline, CancellationToken cancellationToken)
    {
        if (account is null || baseline is null) { _syncedInstanceKeys = new HashSet<string>(StringComparer.Ordinal); return; }
        try { UpdateCloudInstanceState(account, await _local.ExportAsync(cancellationToken), baseline); }
        catch (Exception) { _syncedInstanceKeys = new HashSet<string>(StringComparer.Ordinal); }
    }

    /// <summary>原子替换不可变状态快照；新建、编辑及关联标签变化都不能沿用旧的云端标记。</summary>
    private void UpdateCloudInstanceState(HermodAccount? account, WorkspaceSnapshot local, WorkspaceSnapshot? baseline)
    {
        var synced = new HashSet<string>(StringComparer.Ordinal);
        if (account is not null && baseline is not null)
        {
            string owner = Owner(account);
            var cloudInstances = baseline.Instances.ToDictionary(instance => instance.Id, StringComparer.Ordinal);
            foreach (var instance in local.Instances)
                if (cloudInstances.TryGetValue(instance.Id, out var cloud) &&
                    WorkspaceContent.Equal(InstanceContent(local, instance), InstanceContent(baseline, cloud)))
                    synced.Add(owner + ":" + instance.Id);
        }
        _syncedInstanceKeys = synced;
    }

    /// <summary>关联标签及其祖先属于当前实例的展示配置，排序变化不影响内容比较。</summary>
    private static WorkspaceSnapshot InstanceContent(WorkspaceSnapshot workspace, SyncedInstance instance)
    {
        var tagIds = instance.TagIds.ToHashSet(StringComparer.Ordinal);
        int previousCount;
        do
        {
            previousCount = tagIds.Count;
            foreach (var tag in workspace.Tags.Where(tag => tagIds.Contains(tag.Id)))
                if (tag.ParentId is not null) tagIds.Add(tag.ParentId);
        } while (tagIds.Count != previousCount);
        return new(workspace.SchemaVersion, [instance], workspace.Tags.Where(tag => tagIds.Contains(tag.Id)).ToArray());
    }
}
