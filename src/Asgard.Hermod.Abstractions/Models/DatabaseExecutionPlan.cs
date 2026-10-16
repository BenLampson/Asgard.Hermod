namespace Asgard.Hermod.Abstractions.Models;

/// <summary>一次计划请求的证据快照；阅读操作不持有连接，不会重新执行 SQL。</summary>
public sealed record DatabaseExecutionPlan
{
    /// <summary>完整服务器输出；解析失败时仍保留。</summary>
    public required string RawText { get; init; }
    /// <summary>被分析的原始 SQL，不包含界面后来改动。</summary>
    public required string Sql { get; init; }
    /// <summary>该请求实际选择的数据库。</summary>
    public required string Database { get; init; }
    /// <summary>是否为确认后的实际分析请求；执行是否成功由 RequestSucceeded 独立表示。</summary>
    public bool IsActual { get; init; }
    /// <summary>请求成功与计划格式独立，失败原文不能被标成成功执行。</summary>
    public bool RequestSucceeded { get; init; } = true;
    /// <summary>计划请求的客户端耗时，不作为节点时间。</summary>
    public TimeSpan RequestElapsed { get; init; }
    /// <summary>解析成功的真实结构或原始回退。</summary>
    public DatabasePlanFormat Format { get; init; }
    /// <summary>服务器树根，顺序保持不变。</summary>
    public IReadOnlyList<DatabasePlanNode> Roots { get; init; } = [];
    /// <summary>传统字段表的原始列名。</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];
    /// <summary>传统字段表的原始值；null 保留 SQL NULL 语义。</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; init; } = [];
    /// <summary>是否能够展示服务器提供的结构。</summary>
    public bool HasStructure => Format == DatabasePlanFormat.Tree && Roots.Count > 0 || Format == DatabasePlanFormat.Fields && Columns.Count > 0;
}
