namespace Asgard.Hermod.MySql;

/// <summary>有界结果集保留原始 CLR 值与列顺序，绝不以省略号覆盖原始数据。</summary>
public sealed record MySqlResult(int Statement, int ResultIndex, IReadOnlyList<MySqlColumn> Columns,
    IReadOnlyList<object?[]> Rows, bool IsTruncated = false, MySqlTable? EditableTable = null, bool HasMore = false);
