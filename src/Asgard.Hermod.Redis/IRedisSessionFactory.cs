namespace Asgard.Hermod.Redis;

/// <summary>工作页与真实连接测试使用相同协议入口。</summary>
public interface IRedisSessionFactory
{
    /// <summary>初始化拓扑、认证和目标数据库；失败时释放所有连接。</summary>
    Task<IRedisSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default);
}
