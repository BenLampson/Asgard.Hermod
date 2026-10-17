namespace Asgard.Hermod.UI.Models;

/// <summary>执行记录阅读器只按稳定操作类型筛选内存快照，不调用协议模块。</summary>
public interface IDatabaseExecutionTrace
{
    /// <summary>查询、估算、实际分析等稳定标识。</summary>
    string Kind { get; }
}
