namespace Asgard.Hermod.Abstractions.Models;

/// <summary>不可变的服务器计划节点；标识、表达式和原始位置与界面语言无关。</summary>
public sealed record DatabasePlanNode
{
    /// <summary>父子路径生成的稳定节点标识。</summary>
    public required string Id { get; init; }
    /// <summary>已识别的算子类型，仅决定本地化标签。</summary>
    public DatabasePlanOperation Operation { get; init; }
    /// <summary>服务器原始算子名称，不翻译。</summary>
    public required string ServerOperator { get; init; }
    /// <summary>完整的服务器算子表达式，不拼接到任何可执行命令。</summary>
    public required string FullOperation { get; init; }
    /// <summary>便于阅读的原始访问对象或条件。</summary>
    public string Expression { get; init; } = "";
    /// <summary>服务器提供的数字及执行事实。</summary>
    public DatabasePlanMetrics Metrics { get; init; } = new();
    /// <summary>只遵循服务器顺序，不按时间重排。</summary>
    public IReadOnlyList<DatabasePlanNode> Children { get; init; } = [];
    /// <summary>完整原始字段，键值均不翻译。</summary>
    public IReadOnlyDictionary<string, string> RawFields { get; init; } = new Dictionary<string, string>();
    /// <summary>节点在完整原始文本中的字符位置；不可定位时为空。</summary>
    public int? SourceOffset { get; init; }
    /// <summary>原始节点字符长度。</summary>
    public int SourceLength { get; init; }
}
