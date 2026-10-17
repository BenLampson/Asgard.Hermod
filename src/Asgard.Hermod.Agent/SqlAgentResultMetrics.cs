namespace Asgard.Hermod.Agent;

/// <summary>已加载结果的本地只读指标；不表示表的总行数，不包含凭据或 SQL。</summary>
public sealed record SqlAgentResultMetrics(int Rows, int Columns, TimeSpan Elapsed, bool IsTruncated);
