namespace Asgard.Hermod.Agent;

/// <summary>仅含安全状态和有界结果样本，未知结果不得自动重放。</summary>
public sealed record SqlAgentResult(string Status, string Output, bool StopActions = false)
{
    /// <summary>本地证据阅读使用已完成请求的真实指标；模型协议仍只发送状态和有界输出。</summary>
    public SqlAgentResultMetrics? Metrics { get; init; }
}
