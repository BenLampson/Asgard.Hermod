namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>查询页签内存中的有界执行追踪，不持久化 SQL、计划或查询结果。</summary>
public sealed record PostgreSqlExecutionTrace(DateTimeOffset Started, string Kind, string Database, string Sql, TimeSpan Elapsed, int Rows, bool Succeeded, string Detail)
{
    /// <summary>时间按区域格式展示，操作类型和结果说明使用共享资源。</summary>
    public string Summary => Text.Format("PostgreSqlTraceSummary", Started.ToLocalTime().ToString("T", CultureInfo.CurrentCulture),
        Text.Get(Kind switch { "explain" => "PostgreSqlExplain", "analyze" => "PostgreSqlAnalyze", _ => "PostgreSqlTraceQuery" }), Elapsed.TotalMilliseconds, Rows,
        Text.Get(Succeeded ? "PostgreSqlTraceSucceeded" : "PostgreSqlTraceFailed"));
}
