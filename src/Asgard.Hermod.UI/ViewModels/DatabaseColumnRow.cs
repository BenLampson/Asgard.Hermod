namespace Asgard.Hermod.UI.ViewModels;

/// <summary>结构表格只包装协议元数据，未知属性明确标注，不解析 Detail 或拼接可执行 SQL。</summary>
public sealed class DatabaseColumnRow(string name, string type, DatabaseColumnDefinition? definition)
{
    /// <summary>原始字段名称。</summary>
    public string Name { get; } = name;
    /// <summary>服务器数据类型，不翻译协议类型名。</summary>
    public string DataType => definition?.DataType ?? type;
    /// <summary>没有报告可空性时不默认为允许 NULL。</summary>
    public string NullableLabel => Text.Get(definition?.Nullable switch { true => "DatabaseColumnAllowsNull", false => "DatabaseColumnRequired", _ => "DatabaseMetadataUnavailable" });
    /// <summary>键类别表示成员身份；不用于控制是否可以写入。</summary>
    public string ConstraintLabel => Text.Get(definition?.KeyKind switch
    {
        DatabaseColumnKeyKind.Primary => "DatabaseColumnPrimaryKey",
        DatabaseColumnKeyKind.Unique => "DatabaseColumnUniqueKey",
        DatabaseColumnKeyKind.Indexed => "DatabaseColumnIndex",
        DatabaseColumnKeyKind.None => "DatabaseMetadataEmpty",
        _ => "DatabaseMetadataUnavailable",
    });
    /// <summary>缺失、空字符串和服务器默认表达式分开展示。</summary>
    public string DefaultLabel => definition?.DefaultValue is not { } value ? Text.Get("DatabaseMetadataUnavailable") : value.Length == 0 ? Text.Get("DatabaseColumnEmptyString") : value;
    /// <summary>原始备注允许为空，不把示例说明填成服务器数据。</summary>
    public string Comment => definition?.Comment ?? "";
    /// <summary>详情中保留字段数据类型、可空性、默认值、额外属性和生成表达式。</summary>
    public string Detail
    {
        get
        {
            string nullable = Text.Get(definition?.Nullable switch { true => "DatabaseColumnSqlNull", false => "DatabaseColumnSqlNotNull", _ => "DatabaseMetadataUnavailable" });
            if (definition?.GenerationExpression is { Length: > 0 } expression) return Text.Format("DatabaseColumnGeneratedDetail", DataType, nullable, definition.Extra, expression);
            bool extra = definition?.Extra is { Length: > 0 };
            string key = definition?.DefaultValue is not null ? extra ? "DatabaseColumnDefinitionWithDefaultExtra" : "DatabaseColumnDefinitionWithDefault" : extra ? "DatabaseColumnDetailWithExtra" : "DatabaseColumnDetail";
            return Text.Format(key, DataType, nullable, DefaultLabel, definition?.Extra ?? "");
        }
    }
}
