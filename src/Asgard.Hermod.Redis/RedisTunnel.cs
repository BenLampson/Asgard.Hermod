using System.Net.Sockets;
using Asgard.Hermod.Ssh;
using StackExchange.Redis.Configuration;

namespace Asgard.Hermod.Redis;

/// <summary>每个实际发现的节点独立穿过代理；SSH 仅支持单节点。</summary>
internal sealed class RedisTunnel(RedisSessionOptions options, ISshTcpStreamFactory ssh, CancellationToken openingToken) : Tunnel
{
    public override ValueTask<EndPoint?> GetSocketConnectEndpointAsync(EndPoint endpoint, CancellationToken cancellationToken) => ValueTask.FromResult<EndPoint?>(null);

    public override async ValueTask<Stream?> BeforeAuthenticateAsync(EndPoint endpoint, ConnectionType connectionType, Socket? socket, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, openingToken);
        cancellationToken = linked.Token;
        var target = endpoint switch { DnsEndPoint dns => new RedisEndpoint(dns.Host, dns.Port), IPEndPoint ip => new RedisEndpoint(ip.Address.ToString(), ip.Port), _ => throw Text.Exception("RedisEndpointInvalid") };
        if (options.Route == "ssh")
        {
            var parameters = options.Connection.Parameters.Where(pair => pair.Key.StartsWith("ssh.", StringComparison.Ordinal))
                .ToDictionary(pair => pair.Key[4..], pair => pair.Value, StringComparer.Ordinal);
            parameters["timeout"] = options.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            return await ssh.OpenAsync(new() { TypeId = SshIds.Type, ProviderId = SshIds.Direct, Parameters = parameters }, target.Host, target.Port, cancellationToken).ConfigureAwait(false);
        }
        var client = new TcpClient();
        try
        {
            int port = int.Parse(options.Value("proxy.port", "1080"), CultureInfo.InvariantCulture);
            await client.ConnectAsync(options.Value("proxy.host"), port, cancellationToken).ConfigureAwait(false);
            var stream = client.GetStream();
            await SshProxyProtocol.ConnectAsync(stream, new SshConnectionOptions
            {
                Route = SshIds.Proxy,
                Target = new() { Host = target.Host, Port = target.Port, User = "", Authentication = SshIds.Password },
                ProxyType = options.Value("proxy.type", "socks5"),
                ProxyUser = options.Value("proxy.user"),
                ProxyPassword = options.Value("proxy.password"),
                RemoteDns = options.Value("proxy.dns", "remote") == "remote",
            }, cancellationToken).ConfigureAwait(false);
            // NetworkStream 拥有 socket，连接成功后不再持有 TcpClient 包装对象。
            return stream;
        }
        catch { client.Dispose(); throw; }
    }
}
