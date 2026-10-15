namespace Asgard.Hermod.PostgreSql;

/// <summary>创建无池化、不自动重连的独立会话。</summary>
public interface IPostgreSqlSessionFactory
{
    /// <summary>按独立认证、TLS 和路径配置建立单个会话。</summary>
    Task<IPostgreSqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default);
}
