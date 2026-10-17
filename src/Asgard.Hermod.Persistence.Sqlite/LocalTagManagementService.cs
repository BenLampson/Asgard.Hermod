namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>标签管理应用服务，将事务验证留在仓储并统一发送成功变更通知。</summary>
public sealed class LocalTagManagementService : ITagManagementService
{
    /// <summary>标签和关联的唯一持久化入口。</summary>
    private readonly ILocalConfigurationRepository _repository;

    /// <summary>服务不依赖任何窗口；设置页和向导可复用同一个实例。</summary>
    public LocalTagManagementService(ILocalConfigurationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    /// <inheritdoc />
    public event EventHandler? TagsChanged;
    /// <inheritdoc />
    public async Task<ManagedTag> RenameAsync(string tagId, string name, CancellationToken cancellationToken = default)
    {
        var result = await Task.Run(() => _repository.RenameTag(tagId, name, cancellationToken), cancellationToken).ConfigureAwait(false);
        NotifyTagsChanged();
        return result;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ManagedTag>> GetTagsAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => _repository.GetSnapshot(cancellationToken).Tags, cancellationToken);

    /// <inheritdoc />
    public async Task<ManagedTag> SaveAsync(string? tagId, TagEditDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ManagedTag tag = await Task.Run(() => _repository.SaveTag(tagId, draft, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        NotifyTagsChanged();
        return tag;
    }

    /// <inheritdoc />
    public Task<TagDeletionPreview> PreviewDeleteAsync(string tagId, CancellationToken cancellationToken = default) =>
        Task.Run(() => _repository.PreviewTagDeletion(tagId, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(string tagId, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => _repository.DeleteTag(tagId, cancellationToken), cancellationToken).ConfigureAwait(false);
        NotifyTagsChanged();
    }

    /// <summary>已提交的数据操作保持成功反馈；单个刷新订阅者异常不能阻断其余订阅者。</summary>
    internal void NotifyTagsChanged()
    {
        EventHandler? handlers = TagsChanged;
        if (handlers is null) return;
        foreach (EventHandler handler in handlers.GetInvocationList())
        {
            try { handler(this, EventArgs.Empty); }
            catch (Exception)
            {
                // 通知用于刷新视图，不属于持久化事务；不输出订阅者可能携带敏感数据的异常。
                // 调用方仍可通过 GetTagsAsync 或 GetInstances 重新取得最新快照。
            }
        }
    }
}
