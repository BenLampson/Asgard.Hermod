namespace Asgard.Hermod.Identity;

/// <summary>标签和关联修改共享账户锁并触发自动同步。</summary>
public sealed class SyncedTagManagementService(ITagManagementService local, IConfigurationSyncService sync) : ITagManagementService
{
    /// <inheritdoc />
    public event EventHandler? TagsChanged { add => local.TagsChanged += value; remove => local.TagsChanged -= value; }
    /// <inheritdoc />
    public Task<IReadOnlyList<ManagedTag>> GetTagsAsync(CancellationToken cancellationToken = default) => local.GetTagsAsync(cancellationToken);
    /// <inheritdoc />
    public async Task<ManagedTag> RenameAsync(string tagId, string name, CancellationToken cancellationToken = default)
    {
        ManagedTag? renamed = null;
        await sync.RunLocalChangeAsync(async () => renamed = await local.RenameAsync(tagId, name, cancellationToken), cancellationToken);
        return renamed!;
    }
    /// <inheritdoc />
    public Task<TagDeletionPreview> PreviewDeleteAsync(string tagId, CancellationToken cancellationToken = default) => local.PreviewDeleteAsync(tagId, cancellationToken);
    /// <inheritdoc />
    public async Task<ManagedTag> SaveAsync(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default)
    {
        ManagedTag? result = null;
        await sync.RunLocalChangeAsync(async () => result = await local.SaveAsync(tagId, draft, cancellationToken), cancellationToken);
        return result!;
    }
    /// <inheritdoc />
    public Task DeleteAsync(string tagId, CancellationToken cancellationToken = default) =>
        sync.RunLocalChangeAsync(() => local.DeleteAsync(tagId, cancellationToken), cancellationToken);
}
