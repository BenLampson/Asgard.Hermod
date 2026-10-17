namespace Asgard.Hermod.PostgreSql;

/// <summary>按 PostgreSQL 词法识别字符串、美元引号和可嵌套注释，保留编辑器原始范围。</summary>
public static class PostgreSqlSql
{
    /// <summary>标识符始终双引号引用，保留大小写和内嵌引号。</summary>
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>standard_conforming_strings 来自会话参数，E 字符串始终支持反斜杠转义。</summary>
    public static IReadOnlyList<PostgreSqlStatement> Split(string sql, bool standardConformingStrings = true)
    {
        var results = new List<PostgreSqlStatement>();
        int start = 0, i = 0, nesting = 0, bodyDepth = 0; char quote = '\0'; string? dollar = null;
        string previousWord = "";
        bool line = false, escape = false, hasSql = false;
        while (i < sql.Length)
        {
            char c = sql[i], next = i + 1 < sql.Length ? sql[i + 1] : '\0';
            if (line) { if (c is '\r' or '\n') line = false; i++; continue; }
            if (nesting > 0)
            {
                if (c == '/' && next == '*') { nesting++; i += 2; }
                else if (c == '*' && next == '/') { nesting--; i += 2; }
                else i++;
                continue;
            }
            if (dollar is not null)
            {
                if (sql.AsSpan(i).StartsWith(dollar, StringComparison.Ordinal)) { i += dollar.Length; dollar = null; }
                else i++;
                continue;
            }
            if (quote != '\0')
            {
                if (escape && c == '\\') { i = Math.Min(i + 2, sql.Length); continue; }
                if (c == quote) { if (next == quote) { i += 2; continue; } quote = '\0'; }
                i++; continue;
            }
            if (c == '-' && next == '-') { line = true; i += 2; continue; }
            if (c == '/' && next == '*') { nesting = 1; i += 2; continue; }
            if (c is '\'' or '"')
            {
                quote = c; escape = c == '\'' && (!standardConformingStrings || i > 0 && sql[i - 1] is 'e' or 'E' && (i < 2 || !Identifier(sql[i - 2])));
                hasSql = true; i++; continue;
            }
            if (c == '$' && (i == 0 || !Identifier(sql[i - 1])))
            {
                int end = i + 1;
                if (end < sql.Length && (char.IsLetter(sql[end]) || sql[end] == '_'))
                    while (end < sql.Length && (char.IsLetterOrDigit(sql[end]) || sql[end] == '_')) end++;
                if (end < sql.Length && sql[end] == '$')
                { dollar = sql[i..(end + 1)]; i = end + 1; hasSql = true; previousWord = ""; continue; }
            }
            // SQL 语言例程可以使用 BEGIN ATOMIC 正文；其中的分号不能拆成独立命令。
            if (char.IsLetter(c) || c == '_')
            {
                int wordStart = i++;
                while (i < sql.Length && Identifier(sql[i])) i++;
                string word = sql[wordStart..i];
                if (word.Equals("ATOMIC", StringComparison.OrdinalIgnoreCase) && previousWord.Equals("BEGIN", StringComparison.OrdinalIgnoreCase)) bodyDepth++;
                else if (bodyDepth > 0 && word.Equals("CASE", StringComparison.OrdinalIgnoreCase)) bodyDepth++;
                else if (bodyDepth > 0 && word.Equals("END", StringComparison.OrdinalIgnoreCase)) bodyDepth--;
                previousWord = word; hasSql = true; continue;
            }
            if (c == ';' && bodyDepth == 0) { Add(i); start = ++i; hasSql = false; previousWord = ""; continue; }
            if (!char.IsWhiteSpace(c)) previousWord = "";
            if (!char.IsWhiteSpace(c)) hasSql = true;
            i++;
        }
        if (quote != '\0' || dollar is not null || nesting != 0 || bodyDepth != 0) throw Text.Exception("PostgreSqlSqlIncomplete");
        Add(sql.Length); return results;

        void Add(int end) { if (hasSql) results.Add(new(sql[start..end].Trim(), start, end - start, ";")); }
    }

    /// <summary>只为单条 SQL 生成不执行查询的 JSON 计划，禁止批次混入第二条写操作。</summary>
    public static string Explain(string sql, bool standardConformingStrings = true)
    {
        var statements = Split(sql, standardConformingStrings);
        if (statements.Count != 1) throw Text.Exception("PostgreSqlExplainSingleStatement");
        return "EXPLAIN (FORMAT JSON) " + statements[0].Sql;
    }

    /// <summary>任意 SQL 结果保守只读；只有显式表浏览可提供完整编辑基线。</summary>
    public static bool IsSimpleProjection(string sql) => false;
    /// <summary>不从用户文本猜测物理表身份。</summary>
    public static (string Database, string Table)? ProjectionSource(string sql) => null;
    private static bool Identifier(char c) => char.IsLetterOrDigit(c) || c is '_' or '$';
}
