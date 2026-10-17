namespace Asgard.Hermod.UI.ViewModels;

/// <summary>传统 EXPLAIN 的单行字段摘要，不推断父子关系或实测指标。</summary>
public sealed class DatabasePlanFieldRowViewModel
{
    private readonly IReadOnlyList<string> _columns;
    private readonly IReadOnlyList<string?> _values;
    internal DatabasePlanFieldRowViewModel(IReadOnlyList<string> columns, IReadOnlyList<string?> values, int? sourceOffset, int sourceLength)
    { _columns = columns; _values = values; SourceOffset = sourceOffset; SourceLength = sourceLength; }
    /// <summary>该行在同一份完整原文中的位置。</summary>
    public int? SourceOffset { get; }
    /// <summary>该行原文长度。</summary>
    public int SourceLength { get; }
    /// <summary>服务器表名，缺失不会补造表对象。</summary>
    public string Name => Value("table");
    /// <summary>服务器查询块编号及原始 select_type。</summary>
    public string QueryBlock => Find("id") is { } id ? Text.Format("PlanQueryBlock", id, Value("select_type")) : Value("select_type");
    /// <summary>服务器原始访问方式。</summary>
    public string Access => Value("type");
    /// <summary>服务器估算行数。</summary>
    public string EstimatedRows => Value("rows");
    /// <summary>实际使用的索引；SQL NULL 不等于缺失字段。</summary>
    public string UsedKey => DatabasePlanNodeViewModel.Number(Find("key"));
    /// <summary>完整服务器附加信息。</summary>
    public string Extra => Value("Extra");
    /// <summary>完整字段与原始顺序，不翻译协议字段名。</summary>
    public IReadOnlyList<DatabasePlanDetail> Fields => _columns.Select((column, index) => new DatabasePlanDetail(column,
        index < _values.Count ? _values[index] ?? Text.Get("PlanSqlNull") : Text.Get("PlanMissingMetric"))).ToArray();
    /// <summary>已识别字段的可读详情，仍只展示真实服务器值。</summary>
    public IReadOnlyList<DatabasePlanDetail> Details => [new(Text.Get("PlanAccessType"), Access), new(Text.Get("PlanPossibleKeys"), Value("possible_keys")),
        new(Text.Get("PlanUsedKey"), UsedKey), new(Text.Get("PlanEstimatedRows"), EstimatedRows), new(Text.Get("PlanExtra"), Extra)];
    private string Value(string name)
    {
        for (int index = 0; index < _columns.Count; index++)
            if (string.Equals(_columns[index], name, StringComparison.OrdinalIgnoreCase)) return index < _values.Count ? _values[index] ?? Text.Get("PlanSqlNull") : Text.Get("PlanMissingMetric");
        return Text.Get("PlanMissingMetric");
    }
    private string? Find(string name)
    {
        for (int index = 0; index < _columns.Count; index++)
            if (string.Equals(_columns[index], name, StringComparison.OrdinalIgnoreCase) && index < _values.Count) return _values[index];
        return null;
    }
}
