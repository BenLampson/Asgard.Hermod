namespace Asgard.Hermod.Abstractions.Models;

/// <summary>只用于本地化已识别算子的稳定类型；未知算子保留服务器原名。</summary>
public enum DatabasePlanOperation
{
    /// <summary>未归类的服务器算子。</summary>
    Unknown,
    /// <summary>排序。</summary>
    Sort,
    /// <summary>过滤。</summary>
    Filter,
    /// <summary>表或顺序扫描。</summary>
    TableScan,
    /// <summary>PostgreSQL 顺序扫描。</summary>
    SequentialScan,
    /// <summary>索引扫描。</summary>
    IndexScan,
    /// <summary>索引范围扫描。</summary>
    IndexRangeScan,
    /// <summary>索引查找。</summary>
    IndexLookup,
    /// <summary>嵌套循环连接。</summary>
    NestedLoop,
    /// <summary>哈希连接。</summary>
    HashJoin,
    /// <summary>合并连接。</summary>
    MergeJoin,
    /// <summary>聚合。</summary>
    Aggregate,
    /// <summary>行数限制。</summary>
    Limit,
    /// <summary>物化。</summary>
    Materialize,
    /// <summary>并行收集。</summary>
    Gather,
    /// <summary>追加子计划。</summary>
    Append,
    /// <summary>位图访问。</summary>
    BitmapScan
}
