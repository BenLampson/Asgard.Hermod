namespace Asgard.Hermod.RabbitMq;

/// <summary>每个实例窗口独立持有传输和认证状态。</summary>
public interface IRabbitMqSessionFactory
{
    /// <summary>认证与管理 API 验证，不要求账号已获某个虚拟主机的资源权限。</summary>
    Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default);
}
