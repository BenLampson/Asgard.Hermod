namespace Asgard.Hermod.UI.Services;

/// <summary>数据库引擎共用的范围交互；没有交互实现时拒绝变更。</summary>
public interface IDatabaseSessionDialogs
{
    /// <summary>固定实例下选择数据库；取消不修改原范围。</summary>
    Task<IReadOnlyList<DatabaseIdentity>?> SelectSessionDatabasesAsync(string instanceName, string endpoint, string sessionName,
        IReadOnlyList<DatabaseIdentity> candidates, IReadOnlyList<DatabaseIdentity> included) => Task.FromResult<IReadOnlyList<DatabaseIdentity>?>(null);
    /// <summary>确认精确的移除对象、影响以及必要的替代执行目标。</summary>
    Task<DatabaseRemovalDecision?> ConfirmDatabaseRemovalAsync(string instanceName, string endpoint, DatabaseRemovalRequest request)
        => Task.FromResult<DatabaseRemovalDecision?>(null);
    /// <summary>关闭页签或清空内容等移除操作必须得到用户明确确认。</summary>
    Task<bool> ConfirmSessionActionAsync(string instanceName, string sessionName, string actionKey, string endpoint = "") => Task.FromResult(false);
}
