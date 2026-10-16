namespace Asgard.Hermod.UI.Models;

/// <summary>SQL 建议自身的执行阶段，不使用本地化文案判断行为。</summary>
public enum AgentSqlExecutionState
{
    /// <summary>尚未请求执行。</summary>
    None,
    /// <summary>等待这一次操作的确认。</summary>
    AwaitingApproval,
    /// <summary>已派发到原 Session。</summary>
    Executing,
    /// <summary>本次执行成功。</summary>
    Succeeded,
    /// <summary>拒绝、插入或取消，未派发。</summary>
    NotExecuted,
    /// <summary>执行或校验失败。</summary>
    Failed,
    /// <summary>派发后被取消，不能把未知结果当作可重试失败。</summary>
    Unknown,
}
