namespace Asgard.Hermod.PostgreSql.UI.ViewModels;

/// <summary>SQL 建议绑定生成时的数据库和物理会话，防止切库或重连后误用旧目标。</summary>
public sealed class PostgreSqlAssistantSql
{
    internal PostgreSqlAssistantSql(string sql, string database, object? sessionIdentity, long scopeRevision = 0)
    { Sql = sql; Database = database; SessionIdentity = sessionIdentity; ScopeRevision = scopeRevision; }
    /// <summary>模型原始 SQL，插入和执行前均由用户检查。</summary>
    public string Sql { get; }
    /// <summary>生成建议时的数据库。</summary>
    public string Database { get; }
    /// <summary>确认与执行结果归属原 SQL 卡片，归档恢复时保留同一对象。</summary>
    public AgentSqlSuggestionExecution Execution { get; } = new();
    internal long ScopeRevision { get; }
    internal object? SessionIdentity { get; }
}
