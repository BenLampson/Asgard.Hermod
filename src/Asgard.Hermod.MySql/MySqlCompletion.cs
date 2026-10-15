using System.Text.RegularExpressions;

namespace Asgard.Hermod.MySql;

/// <summary>纯文本补全只解析常见标识符和 FROM/JOIN 别名，不执行 SQL 或推断结果。</summary>
public static class MySqlCompletion
{
    private static readonly Regex Lex = new(@"(?<comment>--(?=\s|$)[^\r\n]*|\#[^\r\n]*|/\*[\s\S]*?(?:\*/|$))|(?<literal>'(?:''|\\.|[^'])*(?:'|$)|""(?:""""|\\.|[^""])*(?:""|$))|(?<identifier>`(?:``|[^`])*(?:`|$)|[\p{L}\p{N}_$]+)|(?<symbol>[^\s])", RegexOptions.Compiled, TimeSpan.FromSeconds(1));
    private static readonly string[] Keywords = ["SELECT", "FROM", "WHERE", "JOIN", "LEFT JOIN", "RIGHT JOIN", "INNER JOIN", "ON", "AS", "AND", "OR", "NOT", "NULL", "IS NULL", "IS NOT NULL", "IN", "EXISTS", "LIKE", "BETWEEN", "GROUP BY", "ORDER BY", "HAVING", "LIMIT", "OFFSET", "DISTINCT", "COUNT", "SUM", "AVG", "MIN", "MAX", "CASE", "WHEN", "THEN", "ELSE", "END", "UNION ALL", "WITH", "INSERT INTO", "UPDATE", "DELETE FROM", "SET", "VALUES", "EXPLAIN"];
    private static readonly HashSet<string> Reserved = new(Keywords.SelectMany(word => word.Split(' ')).Concat(["CROSS", "OUTER", "USING", "UNION", "FOR", "LOCK", "WINDOW"]), StringComparer.OrdinalIgnoreCase);

    /// <summary>识别查询直接引用的表；字段元数据只针对有限的实际引用按需加载。</summary>
    public static IReadOnlyList<(string Database, string Table, string Alias)> Sources(string sql, string database)
    {
        var tokens = Tokens(sql); var sources = new List<(string, string, string)>();
        for (int i = 0; i + 1 < tokens.Count; i++)
        {
            if (!Is(tokens[i], "FROM") && !Is(tokens[i], "JOIN") && !Is(tokens[i], "UPDATE") && !Is(tokens[i], "INTO")) continue;
            int cursor = i + 1;
            if (!tokens[cursor].Groups["identifier"].Success) continue;
            string db = database, table = Name(tokens[cursor++].Value);
            if (cursor + 1 < tokens.Count && tokens[cursor].Value == "." && tokens[cursor + 1].Groups["identifier"].Success)
            { db = table; table = Name(tokens[cursor + 1].Value); cursor += 2; }
            if (cursor < tokens.Count && Is(tokens[cursor], "AS")) cursor++;
            string alias = cursor < tokens.Count && tokens[cursor].Groups["identifier"].Success && !Reserved.Contains(tokens[cursor].Value) ? Name(tokens[cursor].Value) : table;
            if (db.Length > 0) sources.Add((db, table, alias));
        }
        return sources.Distinct().Take(12).ToArray();
    }

    /// <summary>按当前词段或选区返回最多 100 个候选，字符串和注释内部不弹出建议。</summary>
    public static IReadOnlyList<MySqlCompletionCandidate> Suggest(string sql, int caret, int selectionStart, int selectionLength, string database, IReadOnlyList<MySqlObject> metadata)
    {
        caret = Math.Clamp(caret, 0, sql.Length);
        var all = Lex.Matches(sql).Cast<Match>().ToArray();
        if (all.Any(match => (match.Groups["comment"].Success || match.Groups["literal"].Success) && caret > match.Index && caret <= match.Index + match.Length)) return [];
        int start = caret, end = caret;
        var current = all.LastOrDefault(match => match.Groups["identifier"].Success && caret > match.Index && caret <= match.Index + match.Length);
        if (current is not null) { start = current.Index; end = current.Index + current.Length; }
        if (selectionLength > 0) { start = Math.Clamp(selectionStart, 0, sql.Length); end = Math.Clamp(start + selectionLength, start, sql.Length); }
        string prefix = selectionLength > 0 ? "" : sql[start..caret].TrimStart('`');
        var before = all.Where(match => match.Index + match.Length <= start && !match.Groups["comment"].Success && !match.Groups["literal"].Success).ToArray();
        string qualifier = before.Length >= 2 && before[^1].Value == "." ? Name(before[^2].Value) : "";
        var sources = Sources(sql, database);
        var suggestions = new List<MySqlCompletionCandidate>();
        void Add(string name, MySqlObjectKind? kind, string detail = "")
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) suggestions.Add(new(name, kind is null ? name : MySqlSql.Quote(name), start, end - start, kind, detail));
        }
        if (qualifier.Length == 0)
        {
            foreach (string keyword in Keywords) Add(keyword, null);
            foreach (var item in metadata.Where(item => item.Kind == MySqlObjectKind.Database || item.Kind is MySqlObjectKind.Table or MySqlObjectKind.View && item.Database == database)) Add(item.Name, item.Kind, item.Database);
            foreach (var source in sources)
                foreach (var item in metadata.Where(item => item.Kind == MySqlObjectKind.Column && item.Database == source.Database && item.ParentName == source.Table)) Add(item.Name, item.Kind, source.Alias + " · " + item.Detail);
        }
        else
        {
            var source = sources.FirstOrDefault(item => item.Alias.Equals(qualifier, StringComparison.OrdinalIgnoreCase) || item.Table.Equals(qualifier, StringComparison.OrdinalIgnoreCase));
            foreach (var item in metadata)
            {
                if (item.Kind == MySqlObjectKind.Column && item.Database == source.Database && item.ParentName == source.Table) Add(item.Name, item.Kind, item.Detail);
                else if (item.Kind is MySqlObjectKind.Table or MySqlObjectKind.View && item.Database.Equals(qualifier, StringComparison.OrdinalIgnoreCase)) Add(item.Name, item.Kind, item.Database);
            }
        }
        return suggestions.DistinctBy(item => item.InsertText, StringComparer.OrdinalIgnoreCase).OrderBy(item => item.Kind is null).ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
    }

    private static List<Match> Tokens(string sql) => Lex.Matches(sql).Cast<Match>().Where(match => !match.Groups["comment"].Success && !match.Groups["literal"].Success).ToList();
    private static bool Is(Match match, string keyword) => match.Value.Equals(keyword, StringComparison.OrdinalIgnoreCase);
    private static string Name(string token) => token.StartsWith('`') ? (token.Length > 1 && token.EndsWith('`') ? token[1..^1] : token[1..]).Replace("``", "`", StringComparison.Ordinal) : token;
}
