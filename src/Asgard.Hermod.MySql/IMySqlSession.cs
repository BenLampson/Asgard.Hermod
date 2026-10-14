namespace Asgard.Hermod.MySql;

/// <summary>一个物理会话串行执行命令；查询页签分别拥有会话与事务上下文。</summary>
public interface IMySqlSession : IAsyncDisposable
{
    MySqlSessionOptions Options { get; }
    bool IsConnected { get; }
    MySqlSessionState State { get; }
    Task<MySqlObjectPage> ListAsync(MySqlObject parent, int offset, CancellationToken token = default);
    Task<MySqlTable> DescribeAsync(string database, string table, CancellationToken token = default);
    Task<MySqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default);
    Task<MySqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default);
    Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(MySqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default);
    Task SaveAsync(MySqlTable table, IReadOnlyList<MySqlRowChange> changes, CancellationToken token = default);
    Task<MySqlSessionState> ReadStateAsync(CancellationToken token = default);
    Task RollbackAsync(CancellationToken token = default);
}
