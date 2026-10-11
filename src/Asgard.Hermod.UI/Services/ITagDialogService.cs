namespace Asgard.Hermod.UI.Services;

/// <summary>隔离标签管理状态与编辑、删除窗口的创建及所属窗口关系。</summary>
public interface ITagDialogService
{
    /// <summary>编辑现有标签或创建标签；取消时返回空，添加子级时预填父级。</summary>
    Task<ManagedTag?> EditAsync(ManagedTag? tag = null, string? parentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>显示完整删除影响，用户确认并删除成功后返回真。</summary>
    Task<bool> DeleteAsync(ManagedTag tag, CancellationToken cancellationToken = default);
}
