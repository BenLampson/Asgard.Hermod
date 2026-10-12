using System.Net;
using System.Net.Sockets;

namespace Asgard.Hermod.Identity;

/// <summary>资源请求固定可信根地址；可在 DNS 传播期间指定连接 IP，TLS 身份仍由域名验证。</summary>
public static class HermodApiTransport
{
    /// <summary>只改变 TCP 目标，不关闭证书校验，也不跟随携带 Bearer 的重定向。</summary>
    public static HttpMessageHandler CreateHandler(HermodIdentityOptions options)
    {
        if (string.IsNullOrEmpty(options.ApiServerAddress)) return new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        var address = IPAddress.Parse(options.ApiServerAddress);
        var endpoint = new Uri(options.ApiBaseUrl);
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = async (context, cancellationToken) =>
            {
                if (!string.Equals(context.DnsEndPoint.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase) || context.DnsEndPoint.Port != endpoint.Port)
                    throw Text.Exception("IdentityConfigurationInvalid");
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, endpoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            },
        };
    }
}
