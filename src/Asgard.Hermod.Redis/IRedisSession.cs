namespace Asgard.Hermod.Redis;

/// <summary>协议接口不引用 WPF；测试可以模拟慢读取和失效结果。</summary>
public interface IRedisSession : IAsyncDisposable
{
    /// <summary>经过验证的连接配置。</summary>
    RedisSessionOptions Options { get; }
    /// <summary>各主节点稳定标识，SCAN 游标必须按节点隔离。</summary>
    IReadOnlyList<string> ScanNodes { get; }
    /// <summary>服务器实际数据库数量，Cluster 固定为一。</summary>
    int DatabaseCount => 1;
    /// <summary>按主节点读取一个数据库的实际键总数；权限不足或节点不可达时返回 null。</summary>
    Task<long?> ReadDatabaseKeyCountAsync(int database, CancellationToken cancellationToken = default);
    /// <summary>每次仅请求一个 SCAN 批次，空批次不代表完成。</summary>
    Task<RedisScanPage> ScanAsync(string node, int database, string cursor, string pattern, string type, CancellationToken cancellationToken);
    /// <summary>有界读取一页及元数据；不获取创建时间。</summary>
    Task<RedisSnapshot> ReadAsync(int database, RedisKeyId key, string cursor = "0", long offset = 0, CancellationToken cancellationToken = default);
    /// <summary>原子取得有界编辑基线；大值只读。</summary>
    Task<RedisSnapshot> ReadEditableAsync(int database, RedisKeyId key, CancellationToken cancellationToken = default);
    /// <summary>比较基线后原子修改；保留服务器当前的绝对到期时间。</summary>
    Task SaveAsync(int database, RedisKeyId key, RedisSnapshot baseline, RedisSnapshot draft, CancellationToken cancellationToken = default);
    /// <summary>仅在键不存在时原子创建初始值与 TTL；null 表示永久，冲突不修改已有键。</summary>
    Task CreateAsync(int database, RedisKeyId key, RedisSnapshot initial, TimeSpan? expiry, CancellationToken cancellationToken = default);
    /// <summary>仅删除基线仍匹配的当前键。</summary>
    Task DeleteAsync(int database, RedisKeyId key, RedisSnapshot baseline, CancellationToken cancellationToken = default);
    /// <summary>仅调整现有键的 TTL；null 表示永久。</summary>
    Task SetExpiryAsync(int database, RedisKeyId key, TimeSpan? expiry, CancellationToken cancellationToken = default);
    /// <summary>Stream 只追加新的字段记录，不确认消费或裁剪。</summary>
    Task AppendAsync(int database, RedisKeyId key, IReadOnlyList<RedisEntry> fields, CancellationToken cancellationToken = default);
}
