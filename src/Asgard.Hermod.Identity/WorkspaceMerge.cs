namespace Asgard.Hermod.Identity;

/// <summary>以共同基准逐字段合并；新增、删除和同字段分歧使用稳定冲突键。</summary>
public sealed partial class WorkspaceMerge
{
    private readonly List<ConfigurationConflict> _conflicts = [];
    private IReadOnlyDictionary<string, bool> _choices = new Dictionary<string, bool>();
    /// <summary>这次合并需要用户决定的安全预览。</summary>
    public IReadOnlyList<ConfigurationConflict> Conflicts => _conflicts;

    /// <summary>相同最终内容无冲突；没有共同基准时只自动合并互不重叠的新增。</summary>
    public WorkspaceSnapshot Merge(WorkspaceSnapshot? baseline, WorkspaceSnapshot local, WorkspaceSnapshot cloud,
        IReadOnlyDictionary<string, bool>? choices = null)
    {
        _conflicts.Clear(); _choices = choices ?? new Dictionary<string, bool>();
        var instances = MergeItems(baseline?.Instances ?? [], local.Instances, cloud.Instances, item => item.Id,
            item => item.Name, MergeInstance, InstanceEqual, "instance");
        var tags = MergeItems(baseline?.Tags ?? [], local.Tags, cloud.Tags, item => item.Id,
            item => item.Name, MergeTag, (a, b) => a == b, "tag");
        return new(1, instances, tags) { AgentModels = MergeAgentModels(baseline?.AgentModels, local.AgentModels, cloud.AgentModels) };
    }

    /// <summary>物理删除结合基准判断，避免旧设备复活已删除条目。</summary>
    private IReadOnlyList<T> MergeItems<T>(IReadOnlyList<T> baseline, IReadOnlyList<T> local, IReadOnlyList<T> cloud,
        Func<T, string> id, Func<T, string> name, Func<T, T, T, T> merge, Func<T?, T?, bool> equal, string kind) where T : class
    {
        var b = baseline.ToDictionary(id); var l = local.ToDictionary(id); var c = cloud.ToDictionary(id);
        var result = new List<T>();
        foreach (string key in b.Keys.Concat(l.Keys).Concat(c.Keys).Distinct().Order(StringComparer.Ordinal))
        {
            T? bv = b.GetValueOrDefault(key), lv = l.GetValueOrDefault(key), cv = c.GetValueOrDefault(key);
            T? value;
            if (equal(lv, cv)) value = lv;
            else if (equal(lv, bv)) value = cv;
            else if (equal(cv, bv)) value = lv;
            else if (bv is not null && lv is not null && cv is not null) value = merge(bv, lv, cv);
            else value = Choose(kind + ":" + key + ":entry", name(lv ?? cv ?? bv!), "SyncConflictEntry", lv, cv,
                lv is null ? Text.Get("SyncDeleted") : name(lv), cv is null ? Text.Get("SyncDeleted") : name(cv));
            if (value is not null) result.Add(value);
        }
        return result;
    }

    /// <summary>连接参数逐键比较，标签关联逐成员比较；所有参数预览遮蔽。</summary>
    private SyncedInstance MergeInstance(SyncedInstance b, SyncedInstance l, SyncedInstance c)
    {
        string prefix = "instance:" + l.Id + ":";
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string key in b.Parameters.Keys.Concat(l.Parameters.Keys).Concat(c.Parameters.Keys).Distinct())
        {
            string? value = Field(prefix + "parameter:" + key, l.Name, "SyncConflictParameters",
                b.Parameters.GetValueOrDefault(key), l.Parameters.GetValueOrDefault(key), c.Parameters.GetValueOrDefault(key), true);
            if (value is not null) parameters[key] = value;
        }
        string[] tagIds = b.TagIds.Concat(l.TagIds).Concat(c.TagIds).Distinct().Where(key =>
            l.TagIds.Contains(key) == c.TagIds.Contains(key) ? l.TagIds.Contains(key)
                : l.TagIds.Contains(key) == b.TagIds.Contains(key) ? c.TagIds.Contains(key) : l.TagIds.Contains(key)).ToArray();
        // 托管品牌独立合并，不把展示修改变成连接器或凭据变化。
        return l with
        {
            Name = Field(prefix + "name", l.Name, "InstanceName", b.Name, l.Name, c.Name)!,
            TypeId = Field(prefix + "type", l.Name, "TypeProvider", b.TypeId, l.TypeId, c.TypeId)!,
            ProviderId = Field(prefix + "provider", l.Name, "TypeProvider", b.ProviderId, l.ProviderId, c.ProviderId)!,
            HostingProviderId = MergeHostingProvider(prefix, b, l, c),
            Parameters = parameters,
            TagIds = tagIds,
            CreatedAt = b.CreatedAt,
        };
    }

    /// <summary>品牌冲突展示本地化名称，不把稳定标识当成用户文案。</summary>
    private string MergeHostingProvider(string prefix, SyncedInstance b, SyncedInstance l, SyncedInstance c)
    {
        if (l.HostingProviderId == c.HostingProviderId) return l.HostingProviderId;
        if (l.HostingProviderId == b.HostingProviderId) return c.HostingProviderId;
        if (c.HostingProviderId == b.HostingProviderId) return l.HostingProviderId;
        return Choose(prefix + "hosting", l.Name, "HostingProvider", l.HostingProviderId, c.HostingProviderId,
            InstanceHostingProviders.Get(l.HostingProviderId).Name, InstanceHostingProviders.Get(c.HostingProviderId).Name)!;
    }

    /// <summary>标签改名、颜色及父级可以独立合并。</summary>
    private SyncedTag MergeTag(SyncedTag b, SyncedTag l, SyncedTag c)
    {
        string prefix = "tag:" + l.Id + ":";
        return l with
        {
            Name = Field(prefix + "name", l.Name, "SyncConflictTagName", b.Name, l.Name, c.Name)!,
            ParentId = Field(prefix + "parent", l.Name, "SyncConflictParent", b.ParentId, l.ParentId, c.ParentId),
            Color = Field(prefix + "color", l.Name, "SyncConflictColor", b.Color, l.Color, c.Color)!,
            IsBuiltIn = b.IsBuiltIn,
        };
    }

    /// <summary>三方值都不同才需要选择，选择缺失时保留本地候选但禁止提交。</summary>
    private string? Field(string key, string name, string field, string? baseline, string? local, string? cloud, bool secret = false)
    {
        if (local == cloud) return local;
        if (local == baseline) return cloud;
        if (cloud == baseline) return local;
        string Preview(string? value) => value is null ? Text.Get("SyncDeleted") : secret ? Text.Get("SyncSecretValue") : value;
        return Choose(key, name, field, local, cloud, Preview(local), Preview(cloud));
    }

    /// <summary>用户选择只绑定当前版本的冲突键。</summary>
    private T? Choose<T>(string key, string name, string field, T? local, T? cloud, string localPreview, string cloudPreview)
    {
        if (_choices.TryGetValue(key, out bool useCloud)) return useCloud ? cloud : local;
        _conflicts.Add(new(key, name, field, localPreview, cloudPreview));
        return local;
    }

    /// <summary>实例比较忽略字典与关联排列。</summary>
    private static bool InstanceEqual(SyncedInstance? a, SyncedInstance? b) =>
        WorkspaceContent.Equal(a is null ? null : new(1, [a], []), b is null ? null : new(1, [b], []));
}
