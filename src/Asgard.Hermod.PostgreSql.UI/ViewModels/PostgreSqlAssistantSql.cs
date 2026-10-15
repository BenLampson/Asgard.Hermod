namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>SQL 建议绑定生成时的数据库和物理会话，防止切库或重连后误用旧目标。</summary>
public sealed class PostgreSqlAssistantSql
{
    internal PostgreSqlAssistantSql(string sql, string database, object? sessionIdentity)
    { Sql = sql; Database = database; SessionIdentity = sessionIdentity; }
    /// <summary>模型原始 SQL，插入和执行前均由用户检查。</summary>
    public string Sql { get; }
    /// <summary>生成建议时的数据库。</summary>
    public string Database { get; }
    internal object? SessionIdentity { get; }
}
