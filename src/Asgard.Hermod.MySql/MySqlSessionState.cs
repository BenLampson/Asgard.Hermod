namespace Asgard.Hermod.MySql;

/// <summary>事务监控不可用时明确报告未知，不根据 SQL 文本猜测。</summary>
public sealed record MySqlSessionState(bool? Autocommit, bool? InTransaction);
