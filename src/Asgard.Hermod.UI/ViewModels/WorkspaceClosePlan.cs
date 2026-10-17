namespace Asgard.Hermod.UI.ViewModels;

/// <summary>预检只收集关闭决定；所有窗口同意后才执行破坏性关闭副作用。</summary>
public sealed class WorkspaceClosePlan(Func<Task> commit)
{
    private Task? _commitTask;
    /// <summary>幂等提交，不允许停止查询、回滚或丢弃在预检时提前发生。</summary>
    public Task CommitAsync() => _commitTask ??= commit();
}
