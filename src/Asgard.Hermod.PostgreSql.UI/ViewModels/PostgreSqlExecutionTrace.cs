using Asgard.Hermod.UI.Models;

namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>查询页签内存中的有界执行追踪，不持久化 SQL、计划或查询结果。</summary>
public sealed record PostgreSqlExecutionTrace(DateTimeOffset Started, string Kind, string Database, string Sql, TimeSpan Elapsed, int Rows, bool Succeeded, string Detail) : IDatabaseExecutionTrace
{
    /// <summary>该次请求的完整计划快照，选择历史不重新查询。</summary>
    public DatabaseExecutionPlan? Plan { get; init; }
    /// <summary>双行列表的独立操作名称，不通过翻译文本判断类型。</summary>
    public string KindLabel => Text.Get(Kind switch { "explain" => "DatabaseOutputPlan", "analyze" => "PostgreSqlAnalyze", _ => "PostgreSqlTraceQuery" });
    /// <summary>发送时刻按当前区域显示，不影响快照标识。</summary>
    public string TimeLabel => Started.ToLocalTime().ToString("T", CultureInfo.CurrentCulture);
    /// <summary>耗时、行数与执行目标来自该次快照。</summary>
    public string Metrics => Text.Format("DatabaseTraceMetrics", Elapsed.TotalMilliseconds, Rows, Database);
    /// <summary>只使用稳定状态选择图标。</summary>
    public string StatusIcon => Succeeded ? "circle-check" : "circle-x";
    /// <summary>时间按区域格式展示，操作类型和结果说明使用共享资源。</summary>
    public string Summary => Text.Format("PostgreSqlTraceSummary", Started.ToLocalTime().ToString("T", CultureInfo.CurrentCulture),
        Text.Get(Kind switch { "explain" => "PostgreSqlExplain", "analyze" => "PostgreSqlAnalyze", _ => "PostgreSqlTraceQuery" }), Elapsed.TotalMilliseconds, Rows,
        Text.Get(Succeeded ? "PostgreSqlTraceSucceeded" : "PostgreSqlTraceFailed"));
}
