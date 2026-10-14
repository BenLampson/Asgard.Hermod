namespace Asgard.Hermod.MySql;

/// <summary>一个物理会话串行执行命令；查询页签分别拥有会话与事务上下文。</summary>
public interface IMySqlSession : IAsyncDisposable
{
    MySqlSessionOptions Options { get; }
    bool IsConnected { get; }
    MySqlSessionState State { get; }
    /// <summary>服务器实际的当前数据库，USE 语句后同步到查询页签。</summary>
    string CurrentDatabase { get; }
    /// <summary>使用服务器当前 SQL_MODE 定位编辑器的语句范围。</summary>
    Task<IReadOnlyList<MySqlStatement>> SplitAsync(string sql, CancellationToken token = default);
    /// <summary>读取对象定义，不自动执行返回的 DDL。</summary>
    Task<string> ReadDefinitionAsync(MySqlObject item, CancellationToken token = default);
    Task<MySqlObjectPage> ListAsync(MySqlObject parent, int offset, CancellationToken token = default);
    Task<MySqlTable> DescribeAsync(string database, string table, CancellationToken token = default);
    Task<MySqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default);
    Task<MySqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default);
    Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(MySqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default);
    Task SaveAsync(MySqlTable table, IReadOnlyList<MySqlRowChange> changes, CancellationToken token = default);
    Task<MySqlSessionState> ReadStateAsync(CancellationToken token = default);
    Task RollbackAsync(CancellationToken token = default);
}
