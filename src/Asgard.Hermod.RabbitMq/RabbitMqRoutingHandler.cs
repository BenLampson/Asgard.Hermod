namespace Asgard.Hermod.RabbitMq;

/// <summary>工厂的无状态路由器，不持有任何实例的凭据、会话或连接池。</summary>
internal sealed class RabbitMqRoutingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        request.Options.TryGetValue(RabbitMqTransport.Key, out var transport)
            ? transport.SendAsync(request, cancellationToken) : throw Text.Exception("RabbitMqConfigurationInvalid");
}
