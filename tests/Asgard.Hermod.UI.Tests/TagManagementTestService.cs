namespace Asgard.Hermod.UI.Tests;

/// <summary>在存储边界注入可恢复失败与真实快照，测试不依赖数据库或文件。</summary>
internal sealed class TagManagementTestService : ITagManagementService
{
    private EventHandler? _tagsChanged;

    /// <summary>记录订阅数，用于验证窗口状态释放后不会再次读取。</summary>
    public event EventHandler? TagsChanged
    {
        add { _tagsChanged += value; SubscriptionCount++; }
        remove { _tagsChanged -= value; SubscriptionCount--; }
    }

    /// <summary>测试可调整的服务快照。</summary>
    internal List<ManagedTag> Tags { get; set; } = TagManagementTestData.Create();
    /// <summary>读取时注入的失败。</summary>
    internal Exception? ReadFailure { get; set; }
    /// <summary>保存时注入的规则或持久化失败。</summary>
    internal Exception? SaveFailure { get; set; }
    /// <summary>预览时注入的失败。</summary>
    internal Exception? PreviewFailure { get; set; }
    /// <summary>删除时注入的持久化失败。</summary>
    internal Exception? DeleteFailure { get; set; }
    /// <summary>下一次读取的可控异步结果，用于取消验证。</summary>
    internal TaskCompletionSource<IReadOnlyList<ManagedTag>>? NextRead { get; set; }
    /// <summary>当前状态订阅数量。</summary>
    internal int SubscriptionCount { get; private set; }
    /// <summary>快照读取次数。</summary>
    internal int ReadCount { get; private set; }
    /// <summary>实际保存调用次数。</summary>
    internal int SaveCount { get; private set; }
    /// <summary>实际预览调用次数。</summary>
    internal int PreviewCount { get; private set; }
    /// <summary>实际删除调用次数。</summary>
    internal int DeleteCount { get; private set; }
    /// <summary>后端去重后的实例影响数量。</summary>
    internal int AffectedInstanceCount { get; set; } = 3;
    /// <summary>最近一次提交的规范草稿。</summary>
    internal TagEditDraft? LastDraft { get; private set; }
    /// <summary>最近一次提交的原始稳定标识。</summary>
    internal string? LastSavedId { get; private set; }

    /// <summary>返回独立快照或等待测试控制的异步结果。</summary>
    public async Task<IReadOnlyList<ManagedTag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReadCount++;
        if (ReadFailure is not null) throw ReadFailure;
        if (NextRead is not null)
        {
            var pending = NextRead;
            NextRead = null;
            return await pending.Task.WaitAsync(cancellationToken);
        }
        return Tags.ToArray();
    }

    /// <summary>记录服务实际接到的草稿，成功时发布共享修改通知。</summary>
    public Task<ManagedTag> SaveAsync(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SaveCount++;
        LastDraft = draft;
        LastSavedId = tagId;
        if (SaveFailure is not null) throw SaveFailure;
        var parent = Tags.FirstOrDefault(tag => tag.Id == draft.ParentId);
        var original = Tags.FirstOrDefault(tag => tag.Id == tagId);
        var result = new ManagedTag(tagId ?? "created", draft.Name, draft.ParentId, draft.Color, false,
            parent is null ? draft.Name : $"{parent.Path}/{draft.Name}", original?.InstanceCount ?? 0);
        Tags.RemoveAll(tag => tag.Id == result.Id);
        Tags.Add(result);
        NotifyChanged();
        return Task.FromResult(result);
    }

    /// <summary>从快照计算待删子树，影响实例数量由测试明确提供。</summary>
    public Task<TagDeletionPreview> PreviewDeleteAsync(string tagId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PreviewCount++;
        if (PreviewFailure is not null) throw PreviewFailure;
        var included = new HashSet<string>(StringComparer.Ordinal) { tagId };
        bool changed;
        do
        {
            changed = false;
            foreach (var tag in Tags)
                if (tag.ParentId is not null && included.Contains(tag.ParentId)) changed |= included.Add(tag.Id);
        } while (changed);
        return Task.FromResult(new TagDeletionPreview(tagId,
            Tags.Where(tag => included.Contains(tag.Id)).ToArray(), AffectedInstanceCount));
    }

    /// <summary>仅移除测试标签树，成功后发布数据源通知。</summary>
    public async Task DeleteAsync(string tagId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteCount++;
        if (DeleteFailure is not null) throw DeleteFailure;
        var preview = await PreviewDeleteAsync(tagId, cancellationToken);
        var ids = preview.Tags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
        Tags.RemoveAll(tag => ids.Contains(tag.Id));
        NotifyChanged();
    }

    /// <summary>模拟其他窗口通过共享来源成功修改数据。</summary>
    internal void NotifyChanged() => _tagsChanged?.Invoke(this, EventArgs.Empty);
}
