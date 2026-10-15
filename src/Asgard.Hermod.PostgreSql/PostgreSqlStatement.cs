namespace Asgard.Hermod.PostgreSql;

/// <summary>保留光标范围和当前分隔符，完整例程执行不拆开其正文。</summary>
public sealed record PostgreSqlStatement(string Sql, int Start, int Length, string Delimiter = ";");
