namespace Asgard.Hermod.MySql;

/// <summary>有界结果集保留原始 CLR 值与列顺序，绝不以省略号覆盖原始数据。</summary>
public sealed record MySqlResult(int Statement, int ResultIndex, IReadOnlyList<MySqlColumn> Columns,
    IReadOnlyList<object?[]> Rows, bool IsTruncated = false, MySqlTable? EditableTable = null);

/// <summary>同一批次已完成的结果不会因为后续语句失败而丢失。</summary>
public sealed record MySqlExecution(IReadOnlyList<MySqlResult> Results, IReadOnlyList<MySqlStatementOutcome> Outcomes,
    MySqlFailure? Failure, TimeSpan Elapsed, MySqlSessionState State);

public sealed record MySqlStatementOutcome(int Statement, long AffectedRows, bool Succeeded);

/// <summary>事务监控不可用时明确报告未知，不根据 SQL 文本猜测。</summary>
public sealed record MySqlSessionState(bool? Autocommit, bool? InTransaction);

/// <summary>编辑仅允许有完整主键的 InnoDB 基表。</summary>
public sealed record MySqlTable(string Database, string Name, IReadOnlyList<MySqlColumn> Columns, string Engine)
{
    public IReadOnlyList<MySqlColumn> Keys => Columns.Where(column => column.PrimaryKey).ToArray();
    public bool SupportsEditing => Engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase) && Keys.Count > 0 && Columns.Any(column => column.CanUpdate && !column.Generated);
}
