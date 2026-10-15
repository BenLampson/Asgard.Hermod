namespace Asgard.Hermod.PostgreSql;

/// <summary>同一批次已完成的结果不会因为后续语句失败而丢失。</summary>
public sealed record PostgreSqlExecution(IReadOnlyList<PostgreSqlResult> Results, IReadOnlyList<PostgreSqlStatementOutcome> Outcomes,
    PostgreSqlFailure? Failure, TimeSpan Elapsed, PostgreSqlSessionState State);
