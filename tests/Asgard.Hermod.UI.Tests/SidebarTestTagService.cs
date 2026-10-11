namespace Asgard.Hermod.UI.Tests;

/// <summary>为目录联动验证提供可按稳定标识替换的标签快照。</summary>
internal sealed class SidebarTestTagService : ITagManagementService
{
    /// <summary>内置目录与尚未关联实例的自定义叶子。</summary>
    public IReadOnlyList<ManagedTag> Tags { get; set; } =
    [
        new("production", "Production", null, "#EF4444", true, "Environment/Production"),
        new("staging", "Staging", null, "#F59E0B", true, "Environment/Staging"),
        new("development", "Development", null, "#22C55E", true, "Environment/Development"),
        new("team", "Team", null, "#3B82F6", false, "Team"),
        new("platform", "Platform", "team", "#3B82F6", false, "Team/Platform", 1),
        new("unused", "Unused", null, "#A855F7", false, "Unused"),
    ];
    /// <inheritdoc />
    public event EventHandler? TagsChanged;
    /// <summary>模拟服务在线程池完成事务后的通知。</summary>
    public void PublishChanges() => TagsChanged?.Invoke(this, EventArgs.Empty);
    /// <inheritdoc />
    public Task<IReadOnlyList<ManagedTag>> GetTagsAsync(CancellationToken cancellationToken = default) => Task.FromResult(Tags);
    /// <inheritdoc />
    public Task<ManagedTag> SaveAsync(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    /// <inheritdoc />
    public Task<TagDeletionPreview> PreviewDeleteAsync(string tagId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    /// <inheritdoc />
    public Task DeleteAsync(string tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
