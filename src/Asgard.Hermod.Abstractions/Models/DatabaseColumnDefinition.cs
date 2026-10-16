namespace Asgard.Hermod.Abstractions.Models;

/// <summary>服务器报告的字段元数据；缺失值与空字符串保持不同，不从显示文字反推约束。</summary>
/// <param name="Ordinal">服务器字段序号。</param>
/// <param name="DataType">服务器原始数据类型。</param>
/// <param name="Nullable">可空性未知时为空。</param>
/// <param name="KeyKind">字段参与的键或索引类型。</param>
/// <param name="DefaultValue">服务器未报告默认值时为空；空字符串仍为真实值。</param>
/// <param name="Extra">服务器额外属性或协议定义的生成方式。</param>
/// <param name="Comment">用户创建的原始字段备注。</param>
public sealed record DatabaseColumnDefinition(int Ordinal, string DataType, bool? Nullable, DatabaseColumnKeyKind KeyKind, string? DefaultValue, string Extra, string Comment)
{
    /// <summary>生成字段的原始表达式，与默认值分开，不把计算表达式冒充默认值。</summary>
    public string GenerationExpression { get; init; } = "";
}
