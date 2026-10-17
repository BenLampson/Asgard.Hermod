namespace Asgard.Hermod.Abstractions.Services;

/// <summary>提供标签规则与持久化操作，管理窗口和实例选择器复用同一来源。</summary>
public interface ITagManagementService
{
    /// <summary>成功修改后通知订阅者刷新标签与实例快照。</summary>
    event EventHandler? TagsChanged;
    /// <summary>读取内置环境标签及全部自定义标签。</summary>
    Task<IReadOnlyList<ManagedTag>> GetTagsAsync(CancellationToken cancellationToken = default);
    /// <summary>只重命名自定义标签，保留稳定标识、当前父级、颜色和关联。</summary>
    Task<ManagedTag> RenameAsync(string tagId, string name, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(Text.Get("TheOperationFailedTryAgain"));
    /// <summary>创建或修改自定义标签，验证同级名称、颜色和父子关系。</summary>
    Task<ManagedTag> SaveAsync(string? tagId, TagEditDraft draft,
        CancellationToken cancellationToken = default);
    /// <summary>读取将被删除的自定义子树与关联影响。</summary>
    Task<TagDeletionPreview> PreviewDeleteAsync(string tagId,
        CancellationToken cancellationToken = default);
    /// <summary>删除子树与相关标签关联，保留实例和连接信息。</summary>
    Task DeleteAsync(string tagId, CancellationToken cancellationToken = default);
}
