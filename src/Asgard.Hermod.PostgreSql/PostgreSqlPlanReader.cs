using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.PostgreSql;

/// <summary>只读取已有的 EXPLAIN JSON；估算成本与实测时间分离，保留全部原始证据。</summary>
public static class PostgreSqlPlanReader
{
    /// <summary>解析一次估算快照；不连接、不执行，未知格式回退完整原文。</summary>
    public static DatabaseExecutionPlan Read(PostgreSqlExecution execution, string sql, string database)
    {
        string raw = Raw(execution.Results);
        var snapshot = new DatabaseExecutionPlan { RawText = raw, Sql = sql, Database = database, RequestElapsed = execution.Elapsed, RequestSucceeded = execution.Failure is null };
        if (execution.Failure is not null || raw.Length == 0) return snapshot;
        try
        {
            using var document = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 256 });
            var locations = PostgreSqlPlanLocationIndex.Read(raw);
            var roots = new List<DatabasePlanNode>(); int count = 0;
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in document.RootElement.EnumerateArray())
                {
                    if (!ReadRoot(item, "/" + (index++).ToString(CultureInfo.InvariantCulture))) return snapshot;
                }
            }
            else if (!ReadRoot(document.RootElement, "")) return snapshot;
            return roots.Count == 0 ? snapshot : snapshot with { Format = DatabasePlanFormat.Tree, Roots = Array.AsReadOnly(roots.ToArray()) };

            bool ReadRoot(JsonElement element, string path)
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("Plan", out var plan)) return false;
                var node = Node(plan, path + "/Plan", roots.Count.ToString(CultureInfo.InvariantCulture), locations, ref count, 0);
                if (node is null) return false; roots.Add(node); return true;
            }
        }
        catch (JsonException) { return snapshot; }
    }

    private static DatabasePlanNode? Node(JsonElement element, string path, string id, IReadOnlyDictionary<string, (int Offset, int Length)> locations, ref int count, int depth)
    {
        if (++count > 10000 || depth > 128 || element.ValueKind != JsonValueKind.Object || String(element, "Node Type") is not { } operation) return null;
        var children = new List<DatabasePlanNode>();
        if (element.TryGetProperty("Plans", out var plans))
        {
            if (plans.ValueKind != JsonValueKind.Array) return null;
            foreach (var child in plans.EnumerateArray())
            {
                string index = children.Count.ToString(CultureInfo.InvariantCulture);
                var parsed = Node(child, path + "/Plans/" + index, id + "/" + index, locations, ref count, depth + 1);
                if (parsed is null) return null; children.Add(parsed);
            }
        }
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            // 重复字段无法对应唯一原文位置，整份计划回退原始证据，避免猜测应采用哪一个值。
            if (fields.ContainsKey(property.Name)) return null;
            fields.Add(property.Name, property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText());
        }
        fields.Remove("Plans");
        bool located = locations.TryGetValue(path, out var location);
        return new()
        {
            Id = id,
            ServerOperator = operation,
            FullOperation = operation,
            Operation = Operation(operation),
            Expression = string.Join(" · ", new[] { String(element, "Relation Name") ?? String(element, "Index Name") ?? RawField(element, "Sort Key"), String(element, "Index Cond"), String(element, "Filter") }.Where(value => !string.IsNullOrEmpty(value))),
            Metrics = new() { EstimatedRows = Numeric(element, "Plan Rows"), EstimatedWidth = Numeric(element, "Plan Width"), StartupCost = Numeric(element, "Startup Cost"), TotalCost = Numeric(element, "Total Cost") },
            Children = Array.AsReadOnly(children.ToArray()),
            RawFields = new ReadOnlyDictionary<string, string>(fields),
            SourceOffset = located ? location.Offset : null,
            SourceLength = located ? location.Length : 0
        };
    }

    private static string? String(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? RawField(JsonElement element, string key) => element.TryGetProperty(key, out var value) ? value.GetRawText() : null;
    private static string? Numeric(JsonElement element, string key) => element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out double number) && double.IsFinite(number) && number >= 0 ? value.GetRawText() : null;
    private static DatabasePlanOperation Operation(string operation) => operation switch
    {
        "Sort" or "Incremental Sort" => DatabasePlanOperation.Sort,
        "Seq Scan" or "Parallel Seq Scan" => DatabasePlanOperation.SequentialScan,
        "Index Scan" or "Index Only Scan" => DatabasePlanOperation.IndexScan,
        "Nested Loop" => DatabasePlanOperation.NestedLoop,
        "Hash Join" => DatabasePlanOperation.HashJoin,
        "Merge Join" => DatabasePlanOperation.MergeJoin,
        "Aggregate" => DatabasePlanOperation.Aggregate,
        "Limit" => DatabasePlanOperation.Limit,
        "Materialize" or "Memoize" => DatabasePlanOperation.Materialize,
        "Gather" or "Gather Merge" => DatabasePlanOperation.Gather,
        "Append" or "Merge Append" => DatabasePlanOperation.Append,
        "Bitmap Heap Scan" or "Bitmap Index Scan" => DatabasePlanOperation.BitmapScan,
        _ => DatabasePlanOperation.Unknown
    };

    private static string Raw(IReadOnlyList<PostgreSqlResult> results)
    {
        if (results is [{ Rows.Count: 1 } single] && single.Rows[0].Length == 1) return Value(single.Rows[0][0]);
        var text = new StringBuilder();
        foreach (var result in results) foreach (var row in result.Rows) text.AppendLine(string.Join('\t', row.Select(Value)));
        return text.ToString();
    }
    private static string Value(object? value) => value switch
    {
        null or DBNull => "",
        JsonDocument document => document.RootElement.GetRawText(),
        JsonElement element => element.GetRawText(),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? ""
    };
}
