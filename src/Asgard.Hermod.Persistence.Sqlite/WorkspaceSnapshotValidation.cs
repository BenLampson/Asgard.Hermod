namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>在触碰本地事务前验证完整快照，内置标签不可由下载改变。</summary>
internal static class WorkspaceSnapshotValidation
{
    private static readonly SyncedTag[] BuiltIns =
    [
        new("builtin.environment.production", "Production", null, "#EF4444", true),
        new("builtin.environment.staging", "Staging", null, "#F59E0B", true),
        new("builtin.environment.development", "Development", null, "#22C55E", true),
    ];

    /// <summary>返回父级在前的标签顺序；拒绝循环、重复标识和悬挂关联。</summary>
    public static IReadOnlyList<StoredTag> Validate(WorkspaceSnapshot snapshot)
    {
        if (snapshot is null || snapshot.SchemaVersion != 1 || snapshot.Instances is null || snapshot.Tags is null
            || snapshot.Instances.Count > 10000 || snapshot.Tags.Count > 10000
            || snapshot.Instances.Any(value => value is null) || snapshot.Tags.Any(value => value is null)) Invalid();
        if (snapshot!.Tags.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Tags.Count
            || snapshot.Instances.Select(value => value.Id).Distinct(StringComparer.Ordinal).Count() != snapshot.Instances.Count
            || !snapshot.Tags.Where(value => value.IsBuiltIn).OrderBy(value => value.Id)
                .SequenceEqual(BuiltIns.OrderBy(value => value.Id))) Invalid();
        foreach (var tag in snapshot.Tags)
            if (!ValidId(tag.Id) || (!tag.IsBuiltIn && tag.Id.StartsWith("builtin.", StringComparison.Ordinal))) Invalid();
        var ordered = new List<StoredTag>(BuiltIns.Select(value => new StoredTag(value.Id, value.Name, value.ParentId, value.Color, true)));
        var remaining = snapshot.Tags.Where(value => !value.IsBuiltIn).ToList();
        while (remaining.Count > 0)
        {
            var ready = remaining.Where(value => value.ParentId is null || ordered.Any(parent => parent.Id == value.ParentId)).ToArray();
            if (ready.Length == 0) Invalid();
            foreach (var tag in ready)
            {
                var draft = TagHierarchy.ValidateEdit(null, new TagEditDraft(tag.Name, tag.ParentId, tag.Color), ordered);
                ordered.Add(new StoredTag(tag.Id, draft.Name, draft.ParentId, draft.Color, false));
                remaining.Remove(tag);
            }
        }
        var tagIds = ordered.Select(value => value.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var instance in snapshot.Instances)
        {
            if (!ValidId(instance.Id) || !ValidId(instance.TypeId) || !ValidId(instance.ProviderId)
                || !InstanceHostingProviders.IsValid(instance.HostingProviderId)
                || instance.HostingProviderId.Length > 0 && !InstanceHostingProviders.Supports(instance.TypeId)
                || string.IsNullOrWhiteSpace(instance.Name) || instance.Name.Length > 128 || instance.Name.Any(char.IsControl)
                || instance.Parameters is null || instance.Parameters.Count > 100
                || instance.Parameters.Any(pair => !ValidId(pair.Key) || pair.Value is null || pair.Value.Length > 65536)
                || instance.CreatedAt.Kind != DateTimeKind.Utc || instance.TagIds is null
                || instance.TagIds.Count > tagIds.Count || instance.TagIds.Any(id => !tagIds.Contains(id))
                || instance.TagIds.Distinct(StringComparer.Ordinal).Count() != instance.TagIds.Count) Invalid();
        }
        try { AgentModelValidation.Validate(snapshot.AgentModels); }
        catch (Exception) { Invalid(); }
        return ordered;
    }
    /// <summary>标识不得为空或包含控制字符，协议稳定字段不作翻译。</summary>
    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 && !value.Any(char.IsControl);
    /// <summary>只暴露安全的资源化错误，不包含下载的配置字段。</summary>
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid() => throw Text.Exception("SyncSnapshotInvalid");
}
