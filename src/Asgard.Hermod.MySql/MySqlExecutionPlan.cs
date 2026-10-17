using System.Text.RegularExpressions;

namespace Asgard.Hermod.MySql;

/// <summary>执行计划只包装已按服务器模式定位的单条 SQL；实际分析必须由界面单独确认。</summary>
public static class MySqlExecutionPlan
{
    private static readonly Regex LeadingComments = new(@"\A(?:\s+|/\*(?![!M])[\s\S]*?\*/|\#[^\r\n]*(?:\r?\n|$)|--(?=\s)[^\r\n]*(?:\r?\n|$))*", RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
    /// <summary>构造 EXPLAIN，拒绝批次、客户端分隔符、可执行注释和分析写语句。</summary>
    public static string Create(IReadOnlyList<MySqlStatement> statements, bool analyze)
    {
        if (statements.Count != 1 || statements[0].Delimiter != ";") throw Text.Exception("MySqlPlanSingleStatement");
        string sql = statements[0].Sql;
        if (sql.Contains("/*!", StringComparison.Ordinal) || sql.Contains("/*M!", StringComparison.OrdinalIgnoreCase)) throw Text.Exception("MySqlPlanUnsupportedStatement");
        string body = LeadingComments.Replace(sql, "", 1);
        string first = new string(body.TakeWhile(character => char.IsLetter(character)).ToArray()).ToUpperInvariant();
        if (!(analyze ? first == "SELECT" : new[] { "SELECT", "WITH", "INSERT", "UPDATE", "DELETE", "REPLACE" }.Contains(first))) throw Text.Exception(analyze ? "MySqlAnalyzeSelectOnly" : "MySqlPlanUnsupportedStatement");
        // 保守排除 SELECT 的文件输出、锁和变量写入；函数仍可能有副作用，确认文案明确说明。
        if (analyze && (Regex.IsMatch(body, @"\b(INTO|FOR\s+UPDATE|LOCK\s+IN\s+SHARE\s+MODE)\b", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1)) || body.Contains(":=", StringComparison.Ordinal))) throw Text.Exception("MySqlAnalyzeSelectOnly");
        return (analyze ? "EXPLAIN ANALYZE " : "EXPLAIN ") + sql;
    }
}
