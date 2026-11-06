namespace Asgard.Hermod.MySql;

/// <summary>对象原名保留协议大小写；分类标题由界面翻译。</summary>
public sealed record MySqlObject(MySqlObjectKind Kind, string Name, string Database = "", string ParentName = "", string Detail = "")
{
    /// <summary>字段结构的原始证据；分类、表和其他对象不伪造字段属性。</summary>
    public DatabaseColumnDefinition? ColumnDefinition { get; init; }

    /// <summary>表的下一个自增值；不可用时为空。</summary>
    public ulong? AutoIncrement { get; init; }
    /// <summary>服务器报告的表修改时间；不可用时为空。</summary>
    public DateTime? UpdatedAt { get; init; }
    /// <summary>服务器报告的数据长度（字节）；不可用时为空。</summary>
    public ulong? DataLength { get; init; }
    /// <summary>服务器报告的存储引擎。</summary>
    public string? Engine { get; init; }
    /// <summary>服务器报告的行数，某些引擎提供的是估计值。</summary>
    public ulong? RowCount { get; init; }
    /// <summary>服务器报告的表注释。</summary>
    public string? Comment { get; init; }
}
