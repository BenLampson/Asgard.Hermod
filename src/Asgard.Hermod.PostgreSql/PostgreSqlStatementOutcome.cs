namespace Asgard.Hermod.PostgreSql;

/// <summary>单条语句的真实影响行数及执行结果；失败后不自动重放。</summary>
public sealed record PostgreSqlStatementOutcome(int Statement, long AffectedRows, bool Succeeded);
