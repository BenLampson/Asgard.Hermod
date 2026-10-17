namespace Asgard.Hermod.Abstractions.Models;

/// <summary>保留服务器数字的原始精度；null 表示缺失，绝不替换为零。</summary>
public sealed record DatabasePlanMetrics
{
    /// <summary>每轮实际返回行数，不乘以循环次数。</summary>
    public string? ActualRows { get; init; }
    /// <summary>每轮平均首行时间，单位毫秒。</summary>
    public string? FirstRowMilliseconds { get; init; }
    /// <summary>包含子节点的每轮平均完成时间，单位毫秒。</summary>
    public string? CompletionMilliseconds { get; init; }
    /// <summary>实际循环次数。</summary>
    public string? Loops { get; init; }
    /// <summary>估算行数，与实际行数独立。</summary>
    public string? EstimatedRows { get; init; }
    /// <summary>服务器估算的行宽，单位字节。</summary>
    public string? EstimatedWidth { get; init; }
    /// <summary>估算启动成本，不是毫秒。</summary>
    public string? StartupCost { get; init; }
    /// <summary>估算总成本，不是毫秒。</summary>
    public string? TotalCost { get; init; }
    /// <summary>服务器明确标记该节点未执行。</summary>
    public bool NeverExecuted { get; init; }
}
