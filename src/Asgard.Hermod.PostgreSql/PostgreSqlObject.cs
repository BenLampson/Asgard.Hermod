namespace Asgard.Hermod.PostgreSql;

/// <summary>对象原名保留协议大小写；分类标题由界面翻译。</summary>
public sealed record PostgreSqlObject(PostgreSqlObjectKind Kind, string Name, string Database = "", string ParentName = "", string Detail = "", string Schema = "")
{
    /// <summary>字段结构的原始证据；分类、表和其他对象不伪造字段属性。</summary>
    public DatabaseColumnDefinition? ColumnDefinition { get; init; }
}
