using System.Text;
using System.Text.RegularExpressions;

namespace Asgard.Hermod.MySql;

/// <summary>只解析已有服务器输出；传统 EXPLAIN 保留字段表，ANALYZE 的缩进箭头才形成树。</summary>
public static class MySqlPlanReader
{
    private const string Number = @"\d+(?:\.\d+)?(?:[eE][+-]?\d+)?";
    private static readonly Regex Actual = new($@"\(actual time=(?<first>{Number})\.\.(?<last>{Number})\s+rows=(?<rows>{Number})\s+loops=(?<loops>{Number})\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex Cost = new($@"\(cost=(?<first>{Number})(?:\.\.(?<last>{Number}))?\s+rows=(?<rows>{Number})\)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    /// <summary>创建独立快照；原始输出不截断，无法识别的树回退原始阅读。</summary>
    public static DatabaseExecutionPlan Read(MySqlExecution execution, string sql, string database, bool analyze)
    {
        var snapshot = new DatabaseExecutionPlan
        {
            RawText = Raw(execution.Results, analyze),
            Sql = sql,
            Database = database,
            IsActual = analyze,
            RequestElapsed = execution.Elapsed,
            RequestSucceeded = execution.Failure is null
        };
        if (execution.Failure is not null || execution.Results.Count == 0) return snapshot;
        if (!analyze)
        {
            // 传统字段并不携带可靠的父子节点关系，不从 id/select_type 等列猜测执行树。
            if (execution.Results.Count != 1 || execution.Results[0] is not { Columns.Count: > 0 } table || table.Rows.Any(row => row.Length != table.Columns.Count)) return snapshot;
            return snapshot with
            {
                Format = DatabasePlanFormat.Fields,
                Columns = Array.AsReadOnly(table.Columns.Select(column => column.Name).ToArray()),
                Rows = Array.AsReadOnly(table.Rows.Select(row => (IReadOnlyList<string?>)Array.AsReadOnly(row.Select(Value).ToArray())).ToArray())
            };
        }
        var roots = ParseTree(snapshot.RawText);
        return roots is null ? snapshot : snapshot with { Format = DatabasePlanFormat.Tree, Roots = roots };
    }

    private static string Raw(IReadOnlyList<MySqlResult> results, bool analyze)
    {
        if (analyze && results is [{ Columns.Count: 1, Rows.Count: 1 } single] && single.Rows[0].Length == 1)
            return Value(single.Rows[0][0]) ?? "";
        var text = new StringBuilder();
        foreach (var result in results)
        {
            text.AppendLine(string.Join('\t', result.Columns.Select(column => column.Name)));
            foreach (var row in result.Rows) text.AppendLine(string.Join('\t', row.Select(value => Value(value) ?? "NULL")));
        }
        return text.ToString();
    }

    private static string? Value(object? value) => value is null or DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);

    private static IReadOnlyList<DatabasePlanNode>? ParseTree(string raw)
    {
        var roots = new List<Node>(); var stack = new Stack<Node>(); Node? current = null; int offset = 0, count = 0;
        foreach (string original in raw.Split('\n'))
        {
            string line = original.TrimEnd('\r'); int leading = line.Length - line.TrimStart(' ', '\t').Length;
            if (line.AsSpan(leading).StartsWith("->", StringComparison.Ordinal))
            {
                if (++count > 10000 || stack.Count > 128) return null;
                string operation = Clean(line[(leading + 2)..]); if (operation.Length == 0) return null;
                int indent = line.AsSpan(0, leading).ToString().Sum(character => character == '\t' ? 4 : 1);
                while (stack.TryPeek(out var ancestor) && ancestor.Indent >= indent) stack.Pop();
                var siblings = stack.TryPeek(out var parent) ? parent.Children : roots;
                current = new()
                {
                    Id = parent is null ? siblings.Count.ToString(CultureInfo.InvariantCulture) : parent.Id + "/" + siblings.Count.ToString(CultureInfo.InvariantCulture),
                    Indent = indent,
                    Operation = operation,
                    Offset = offset + leading,
                    End = offset + line.Length
                };
                siblings.Add(current); stack.Push(current); ApplyMetrics(current, line);
            }
            else if (current is not null && line.Trim().Length > 0)
            {
                ApplyMetrics(current, line); current.End = offset + line.Length;
                string continuation = Clean(line);
                if (continuation.Length > 0) current.Operation += "\n" + continuation;
            }
            offset += original.Length + 1;
        }
        return roots.Count == 0 ? null : Array.AsReadOnly(roots.Select(Freeze).ToArray());
    }

    private static void ApplyMetrics(Node node, string line)
    {
        var actual = Actual.Match(line);
        if (actual.Success) node.Metrics = node.Metrics with
        {
            FirstRowMilliseconds = Numeric(actual.Groups["first"].Value),
            CompletionMilliseconds = Numeric(actual.Groups["last"].Value),
            ActualRows = Numeric(actual.Groups["rows"].Value),
            Loops = Numeric(actual.Groups["loops"].Value)
        };
        var cost = Cost.Match(line);
        if (cost.Success) node.Metrics = node.Metrics with
        {
            EstimatedRows = Numeric(cost.Groups["rows"].Value),
            StartupCost = cost.Groups["last"].Success ? Numeric(cost.Groups["first"].Value) : null,
            TotalCost = Numeric(cost.Groups[cost.Groups["last"].Success ? "last" : "first"].Value)
        };
        if (line.Contains("(never executed)", StringComparison.OrdinalIgnoreCase)) node.Metrics = node.Metrics with { NeverExecuted = true };
    }

    private static string Clean(string line) => Cost.Replace(Actual.Replace(line, ""), "").Replace("(never executed)", "", StringComparison.OrdinalIgnoreCase).Trim();
    private static string? Numeric(string raw) => double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) && value >= 0 ? raw : null;

    private static DatabasePlanNode Freeze(Node node)
    {
        var (kind, name, expression) = Operator(node.Operation);
        return new()
        {
            Id = node.Id,
            Operation = kind,
            ServerOperator = name,
            FullOperation = node.Operation,
            Expression = expression,
            Metrics = node.Metrics,
            SourceOffset = node.Offset,
            SourceLength = node.End - node.Offset,
            Children = Array.AsReadOnly(node.Children.Select(Freeze).ToArray())
        };
    }

    private static (DatabasePlanOperation Kind, string Name, string Expression) Operator(string operation)
    {
        (string Prefix, DatabasePlanOperation Kind)[] names = [
            ("Index range scan on ", DatabasePlanOperation.IndexRangeScan), ("Index lookup on ", DatabasePlanOperation.IndexLookup),
            ("Index scan on ", DatabasePlanOperation.IndexScan), ("Table scan on ", DatabasePlanOperation.TableScan),
            ("Sort:", DatabasePlanOperation.Sort), ("Filter:", DatabasePlanOperation.Filter), ("Nested loop", DatabasePlanOperation.NestedLoop),
            ("Hash join", DatabasePlanOperation.HashJoin), ("Aggregate", DatabasePlanOperation.Aggregate),
            ("Limit:", DatabasePlanOperation.Limit), ("Materialize", DatabasePlanOperation.Materialize)];
        foreach (var (prefix, kind) in names)
            if (operation.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return (kind, prefix.EndsWith(" on ", StringComparison.Ordinal) ? prefix[..^4] : prefix.TrimEnd(':'), operation[prefix.Length..].Trim());
        return (DatabasePlanOperation.Unknown, operation.Split('\n')[0], operation);
    }

    private sealed class Node
    {
        internal required string Id;
        internal required string Operation;
        internal int Indent, Offset, End;
        internal DatabasePlanMetrics Metrics = new();
        internal List<Node> Children { get; } = [];
    }
}
