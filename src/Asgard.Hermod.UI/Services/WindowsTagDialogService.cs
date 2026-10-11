using Asgard.Hermod.UI.Views.Dialogs;

namespace Asgard.Hermod.UI.Services;

/// <summary>集中创建标签子窗口，动态所属窗口使确认窗口始终跟随当前系统设置。</summary>
public sealed class WindowsTagDialogService : ITagDialogService
{
    private readonly Func<Window?> _owner;
    private readonly ITagManagementService _tags;

    /// <summary>宿主传入所属窗口获取函数和统一持久化服务。</summary>
    public WindowsTagDialogService(Func<Window?> owner, ITagManagementService tags)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(tags);
        _owner = owner;
        _tags = tags;
    }

    /// <summary>先读取父级候选，再在当前设置窗口中央打开可重试的编辑表单。</summary>
    public async Task<ManagedTag?> EditAsync(ManagedTag? tag = null, string? parentId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tag?.IsBuiltIn == true) return null;
        using var model = new TagEditorViewModel(_tags, tag, parentId);
        await model.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var window = new TagEditorWindow(model) { Owner = _owner() };
        return window.ShowDialog() == true ? model.Result : null;
    }

    /// <summary>读取完整影响后打开确认窗口，只有用户确认且删除成功才返回真。</summary>
    public async Task<bool> DeleteAsync(ManagedTag tag, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tag);
        cancellationToken.ThrowIfCancellationRequested();
        if (tag.IsBuiltIn) return false;
        using var model = new TagDeletionViewModel(_tags, tag);
        await model.InitializeAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var window = new DeleteTagWindow(model) { Owner = _owner() };
        return window.ShowDialog() == true && model.WasDeleted;
    }
}
