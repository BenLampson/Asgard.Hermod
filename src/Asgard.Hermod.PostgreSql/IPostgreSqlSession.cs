namespace Asgard.Hermod.PostgreSql;

/// <summary>一个物理会话串行执行命令；查询页签分别拥有会话与事务上下文。</summary>
public interface IPostgreSqlSession : IAsyncDisposable
{
    /// <summary>本会话已验证的私有协议选项。</summary>
    PostgreSqlSessionOptions Options { get; }
    /// <summary>物理连接是否可用，失败后不自动重连。</summary>
    bool IsConnected { get; }
    /// <summary>实际传输失效通知，不包含 SDK 错误或凭据；主动释放不通知。</summary>
    event EventHandler? ConnectionLost { add { } remove { } }
    /// <summary>只检查当前连接是否存活，不重连或重放命令。</summary>
    Task<bool> PingAsync(CancellationToken token = default) => Task.FromResult(IsConnected);
    /// <summary>服务器报告的自动提交及事务状态，无法读取时明确未知。</summary>
    PostgreSqlSessionState State { get; }
    /// <summary>服务器实际的当前数据库，切换数据库必须建立新的物理连接。</summary>
    string CurrentDatabase { get; }
    /// <summary>使用服务器字符串转义设置定位编辑器的语句范围。</summary>
    Task<IReadOnlyList<PostgreSqlStatement>> SplitAsync(string sql, CancellationToken token = default);
    /// <summary>读取对象定义，不自动执行返回的 DDL。</summary>
    Task<string> ReadDefinitionAsync(PostgreSqlObject item, CancellationToken token = default);
    /// <summary>读取当前树分支的一页对象，不把失败解释为空。</summary>
    Task<PostgreSqlObjectPage> ListAsync(PostgreSqlObject parent, int offset, CancellationToken token = default);
    /// <summary>读取基表、主键和列权限元数据。</summary>
    Task<PostgreSqlTable> DescribeAsync(string database, string table, CancellationToken token = default);
    /// <summary>执行用户 SQL 并保留已完成结果；此会话之后不可用于网格保存。</summary>
    Task<PostgreSqlExecution> ExecuteAsync(string sql, string database, CancellationToken token = default);
    /// <summary>读取有界表页，视图和不具备编辑资格的对象只读。</summary>
    Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, CancellationToken token = default);
    /// <summary>读取真实表分页；筛选只支持受限参数化条件，不执行任意 SQL。</summary>
    Task<PostgreSqlResult> ReadTableAsync(string database, string table, int offset, int pageSize, string filter, CancellationToken token = default) =>
        pageSize == 200 && string.IsNullOrWhiteSpace(filter) ? ReadTableAsync(database, table, offset, token)
            : throw Text.Exception("PostgreSqlConfigurationInvalid");
    /// <summary>按数据库与独立架构读取表目录。</summary>
    Task<PostgreSqlTable> DescribeAsync(string database, string schema, string table, CancellationToken token = default);
    /// <summary>按显式架构读取表数据。</summary>
    Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, CancellationToken token = default);
    /// <summary>按显式架构执行有界分页和参数化筛选。</summary>
    Task<PostgreSqlResult> ReadTableAsync(string database, string schema, string table, int offset, int pageSize, string filter, CancellationToken token = default);
    /// <summary>读取 JSON 估算计划；不使用 ANALYZE，不执行被解释的语句。</summary>
    Task<PostgreSqlExecution> ExplainAsync(string sql, string database, CancellationToken token = default);
    /// <summary>使用完整主键读取类型化行基线。</summary>
    Task<IReadOnlyDictionary<string, object?>> ReadRowAsync(PostgreSqlTable table, IReadOnlyDictionary<string, object?> key, CancellationToken token = default);
    /// <summary>专用会话内锁定并比较完整原行，事务提交修改列；未知结果禁止重试。</summary>
    Task SaveAsync(PostgreSqlTable table, IReadOnlyList<PostgreSqlRowChange> changes, CancellationToken token = default);
    /// <summary>读取真实遥测，不按用户 SQL 内容猜测事务。</summary>
    Task<PostgreSqlSessionState> ReadStateAsync(CancellationToken token = default);
    /// <summary>显式回滚；不能撤销 DDL 或此前已提交写入。</summary>
    Task RollbackAsync(CancellationToken token = default);
}
