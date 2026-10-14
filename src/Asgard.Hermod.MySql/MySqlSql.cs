using System.Text;

namespace Asgard.Hermod.MySql;

/// <summary>识别 MySQL 引号、注释和客户端 DELIMITER，不按裸分号切割。</summary>
public static class MySqlSql
{
    /// <summary>引用数据库标识符，名称不进入 SQL 字面量。</summary>
    public static string Quote(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";

    /// <summary>基于当前会话 SQL_MODE 解析执行范围；DELIMITER 仅在独立行生效。</summary>
    public static IReadOnlyList<MySqlStatement> Split(string sql, bool noBackslashEscapes = false)
    {
        var statements = new List<MySqlStatement>();
        int start = 0, index = 0; char quote = '\0'; bool lineComment = false, blockComment = false;
        string delimiter = ";";
        while (index < sql.Length)
        {
            char c = sql[index], next = index + 1 < sql.Length ? sql[index + 1] : '\0';
            if (lineComment) { if (c is '\r' or '\n') lineComment = false; index++; continue; }
            if (blockComment) { if (c == '*' && next == '/') { blockComment = false; index += 2; } else index++; continue; }
            if (quote != '\0')
            {
                if (c == '\\' && quote != '`' && !noBackslashEscapes) { index = Math.Min(index + 2, sql.Length); continue; }
                if (c == quote) { if (next == quote) { index += 2; continue; } quote = '\0'; }
                index++; continue;
            }
            bool beginning = index == 0 || sql[index - 1] is '\r' or '\n';
            if (beginning)
            {
                int lineEnd = sql.IndexOf('\n', index); if (lineEnd < 0) lineEnd = sql.Length;
                string line = sql[index..lineEnd].Trim();
                if (line.StartsWith("DELIMITER ", StringComparison.OrdinalIgnoreCase))
                {
                    if (HasSql(sql[start..index])) throw Text.Exception("MySqlDelimiterInvalid");
                    delimiter = line[10..].Trim();
                    if (delimiter.Length is 0 or > 16 || delimiter.Any(char.IsWhiteSpace) || delimiter.Contains('\'')) throw Text.Exception("MySqlDelimiterInvalid");
                    index = Math.Min(lineEnd + 1, sql.Length); start = index; continue;
                }
            }
            if (c == '#' || c == '-' && next == '-' && (index + 2 == sql.Length || char.IsWhiteSpace(sql[index + 2]))) { lineComment = true; index++; continue; }
            if (c == '/' && next == '*') { blockComment = true; index += 2; continue; }
            if (c is '\'' or '"' or '`') { quote = c; index++; continue; }
            if (sql.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
            {
                Add(start, index); index += delimiter.Length; start = index; continue;
            }
            index++;
        }
        if (quote != '\0' || blockComment) throw Text.Exception("MySqlSqlIncomplete");
        Add(start, sql.Length); return statements;

        void Add(int from, int to)
        {
            string text = sql[from..to];
            if (HasSql(text)) statements.Add(new(text.Trim(), from, to - from, delimiter));
        }
    }

    /// <summary>结果可编辑性只接受无运算、连接、子查询和函数的单表投影，再由服务器元数据证实。</summary>
    public static bool IsSimpleProjection(string sql)
    {
        var tokens = Tokens(sql);
        if (tokens.Count == 0 || !tokens[0].Equals("SELECT", StringComparison.OrdinalIgnoreCase)) return false;
        int from = tokens.FindIndex(token => token.Equals("FROM", StringComparison.OrdinalIgnoreCase));
        if (from < 2) return false;
        // 引号中的标识符保留前缀，无法与关键字混淆。
        string[] forbidden = ["JOIN", "UNION", "GROUP", "HAVING", "DISTINCT", "INTO", "FOR", "LOCK", "WITH", "PROCEDURE", "WINDOW"];
        if (tokens.Any(token => forbidden.Contains(token, StringComparer.OrdinalIgnoreCase) || token is "(" or ")")) return false;
        for (int i = 1; i < from; i++)
            if (tokens[i] is "+" or "-" or "/" or "%" or "=" or ":" || tokens[i].StartsWith("'", StringComparison.Ordinal)) return false;
        int end = tokens.FindIndex(from + 1, token => new[] { "WHERE", "ORDER", "LIMIT" }.Contains(token, StringComparer.OrdinalIgnoreCase));
        if (end < 0) end = tokens.Count;
        return !tokens.Skip(from + 1).Take(end - from - 1).Contains(",");
    }

    private static bool HasSql(string sql) => Tokens(sql).Count > 0;
    private static List<string> Tokens(string sql)
    {
        var tokens = new List<string>(); int index = 0;
        while (index < sql.Length)
        {
            char c = sql[index];
            if (char.IsWhiteSpace(c) || c == ';') { index++; continue; }
            if (c == '#' || c == '-' && index + 2 < sql.Length && sql[index + 1] == '-' && char.IsWhiteSpace(sql[index + 2]))
            { while (index < sql.Length && sql[index] != '\n') index++; continue; }
            if (c == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                // 版本注释可以执行代码，不允许其通过可编辑性检查。
                if (index + 2 < sql.Length && sql[index + 2] == '!') tokens.Add("(");
                int end = sql.IndexOf("*/", index + 2, StringComparison.Ordinal); index = end < 0 ? sql.Length : end + 2; continue;
            }
            if (c is '\'' or '"' or '`')
            {
                int start = index++; var value = new StringBuilder();
                while (index < sql.Length)
                {
                    if (sql[index] == '\\' && c != '`') { index = Math.Min(index + 2, sql.Length); continue; }
                    if (sql[index] == c) { index++; if (index < sql.Length && sql[index] == c) { index++; continue; } break; }
                    value.Append(sql[index++]);
                }
                tokens.Add(c == '`' ? "`" + value : "'" + sql[start..index]); continue;
            }
            if (char.IsLetterOrDigit(c) || c is '_' or '$')
            { int start = index++; while (index < sql.Length && (char.IsLetterOrDigit(sql[index]) || sql[index] is '_' or '$')) index++; tokens.Add(sql[start..index]); }
            else { tokens.Add(c.ToString()); index++; }
        }
        return tokens;
    }
}

/// <summary>字符位置用于定位光标语句，不改变用户文本。</summary>
public sealed record MySqlStatement(string Sql, int Start, int Length, string Delimiter = ";");
