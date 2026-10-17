using System.Text.RegularExpressions;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.Abstractions.Models;

/// <summary>自动查询的保守入口限制；数据库层仍必须使用独立只读事务，不能仅凭关键字推断只读。</summary>
public static partial class ReadOnlySqlQuery
{
    /// <summary>拒绝事务控制、可执行注释和文件输出；不确定的 SQL 必须回到人工确认路径。</summary>
    public static void Validate(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql) || sql.Length > 16000 || sql.Contains('\0') || !QueryStart().IsMatch(sql)
            || sql.Contains("/*", StringComparison.Ordinal) || sql.Contains("--", StringComparison.Ordinal) || sql.Contains('#') || UnsafeClause().IsMatch(sql))
            throw Text.Exception("SqlAgentReadOnlyRequired");
    }

    [GeneratedRegex(@"\A\s*(?:SELECT|WITH|SHOW|DESCRIBE|DESC|EXPLAIN)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QueryStart();
    [GeneratedRegex(@"\b(?:INTO|OUTFILE|DUMPFILE|LOCK|LOAD_FILE|PG_READ_FILE|PG_READ_BINARY_FILE|LO_EXPORT|DBLINK(?:_\w+)?|SLEEP|BENCHMARK|PG_SLEEP)\b|\bFOR\s+(?:UPDATE|SHARE)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UnsafeClause();
}
