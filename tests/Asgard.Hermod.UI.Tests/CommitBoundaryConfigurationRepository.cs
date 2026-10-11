namespace Asgard.Hermod.UI.Tests;

/// <summary>模拟写入已提交后快照读取不可用，验证应用服务不会把已保存操作反馈成失败。</summary>
public sealed class CommitBoundaryConfigurationRepository : ILocalConfigurationRepository
{
    /// <summary>提交前可用于准备返回结果的标签快照。</summary>
    private readonly ManagedTag _tag = new("tag-team", "Team", null, "#FFFFFF", false, "Team");

    /// <summary>为失败发生在提交前的测试提供明确边界。</summary>
    public bool FailBeforeCommit { get; init; }
    /// <summary>编辑测试在提交前存在的实例记录。</summary>
    public StoredInstanceConfiguration? ExistingInstance { get; init; }
    /// <summary>记录快照读取次数，确保写入后没有额外读取。</summary>
    public int SnapshotReadCount { get; private set; }
    /// <summary>表示已提交一次持久化变更，而不是仅准备草稿。</summary>
    public bool HasCommitted { get; private set; }
    /// <summary>仓储提交的实例记录，用于验证返回结果与保存记录一致。</summary>
    public StoredInstanceConfiguration? CreatedInstance { get; private set; }
    /// <summary>标签保存的已提交结果。</summary>
    public ManagedTag? SavedTag { get; private set; }
    /// <summary>记录删除是否执行，用于通知异常测试。</summary>
    public bool HasDeletedTag { get; private set; }

    /// <inheritdoc />
    public LocalConfigurationSnapshot GetSnapshot(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SnapshotReadCount++;
        if (HasCommitted || FailBeforeCommit)
            throw new InvalidOperationException("模拟快照读取暂时不可用。");
        return new LocalConfigurationSnapshot(ExistingInstance is null ? [] : [ExistingInstance], [_tag]);
    }

    /// <inheritdoc />
    public StoredInstanceConfiguration? GetInstance(string instanceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return CreatedInstance?.Id == instanceId ? CreatedInstance : null;
    }

    /// <inheritdoc />
    public void CreateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CreatedInstance = instance;
        HasCommitted = true;
    }

    /// <inheritdoc />
    public void UpdateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default) =>
        CreateInstance(instance, cancellationToken);

    /// <inheritdoc />
    public ManagedTag SaveTag(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SavedTag = new ManagedTag(tagId ?? "saved-tag", draft.Name, draft.ParentId, draft.Color, false, draft.Name);
        HasCommitted = true;
        return SavedTag;
    }

    /// <inheritdoc />
    public TagDeletionPreview PreviewTagDeletion(string tagId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new TagDeletionPreview(tagId, [_tag], 0);
    }

    /// <inheritdoc />
    public void DeleteTag(string tagId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HasDeletedTag = true;
        HasCommitted = true;
    }
}
