namespace Asgard.Hermod.UI.Tests;

/// <summary>在窗口边界返回确认结果，记录编辑目标和添加子级预填值。</summary>
internal sealed class TagDialogTestService : ITagDialogService
{
    /// <summary>最近一次编辑的真实标签。</summary>
    internal ManagedTag? LastEditTag { get; private set; }
    /// <summary>最近一次新建时传入的父级。</summary>
    internal string? LastParentId { get; private set; }
    /// <summary>最近一次删除目标。</summary>
    internal ManagedTag? LastDeleteTag { get; private set; }
    /// <summary>按测试场景控制编辑成功与取消。</summary>
    internal Func<ManagedTag?, string?, Task<ManagedTag?>>? EditOperation { get; set; }
    /// <summary>按测试场景控制真实删除与取消。</summary>
    internal Func<ManagedTag, Task<bool>>? DeleteOperation { get; set; }

    /// <summary>记录目标并执行测试提供的确认流程。</summary>
    public Task<ManagedTag?> EditAsync(ManagedTag? tag = null, string? parentId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastEditTag = tag;
        LastParentId = parentId;
        return EditOperation?.Invoke(tag, parentId) ?? Task.FromResult<ManagedTag?>(null);
    }

    /// <summary>记录目标，取消时不会调用真实删除。</summary>
    public Task<bool> DeleteAsync(ManagedTag tag, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastDeleteTag = tag;
        return DeleteOperation?.Invoke(tag) ?? Task.FromResult(false);
    }
}
