using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq;

/// <summary>先认证再发现能力；没有可浏览虚拟主机不算连接失败。</summary>
public sealed class RabbitMqSessionFactory(IHttpClientFactory clients, ISshTcpStreamFactory ssh) : IRabbitMqSessionFactory
{
    /// <inheritdoc />
    public async Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default)
    {
        var parsed = RabbitMqSessionOptions.Parse(options);
        var transport = new RabbitMqTransport(parsed, ssh);
        var session = new RabbitMqSession(parsed, clients.CreateClient(RabbitMqIds.HttpClient), transport);
        try { await session.InitializeAsync(cancellationToken).ConfigureAwait(false); return session; }
        catch { await session.DisposeAsync().ConfigureAwait(false); throw; }
    }
}
