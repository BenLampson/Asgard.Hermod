namespace Asgard.Hermod.MySql;

/// <summary>同一批次已完成的结果不会因为后续语句失败而丢失。</summary>
public sealed record MySqlExecution(IReadOnlyList<MySqlResult> Results, IReadOnlyList<MySqlStatementOutcome> Outcomes,
    MySqlFailure? Failure, TimeSpan Elapsed, MySqlSessionState State);
