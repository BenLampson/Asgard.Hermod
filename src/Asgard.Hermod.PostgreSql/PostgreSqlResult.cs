namespace Asgard.Hermod.PostgreSql;

/// <summary>有界结果集保留原始 CLR 值与列顺序，绝不以省略号覆盖原始数据。</summary>
public sealed record PostgreSqlResult(int Statement, int ResultIndex, IReadOnlyList<PostgreSqlColumn> Columns,
    IReadOnlyList<object?[]> Rows, bool IsTruncated = false, PostgreSqlTable? EditableTable = null, bool HasMore = false);
