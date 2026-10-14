namespace Asgard.Hermod.MySql;

/// <summary>创建无池化、不自动重连的独立会话。</summary>
public interface IMySqlSessionFactory
{
    Task<IMySqlSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default);
}
