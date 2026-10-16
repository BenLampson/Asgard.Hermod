using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Asgard.Hermod.Ssh;

namespace Asgard.Hermod.RabbitMq;

/// <summary>单个实例独占连接池和证书策略，通过工厂客户端转发；没有全局凭据或证书回调。</summary>
internal sealed class RabbitMqTransport : IDisposable
{
    internal static readonly HttpRequestOptionsKey<RabbitMqTransport> Key = new("Hermod.RabbitMq.Transport");
    private readonly HttpMessageInvoker _invoker;
    private readonly X509Certificate2Collection _roots = [];
    internal RabbitMqTransport(RabbitMqSessionOptions options, ISshTcpStreamFactory ssh)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            MaxConnectionsPerServer = 4,
            UseProxy = !options.UsesSsh,
            RequestHeaderEncodingSelector = (name, _) => name == "X-Reason" ? Encoding.UTF8 : null
        };
        try
        {
            if (options.UsesSsh)
                handler.ConnectCallback = async (_, token) => await ssh.OpenAsync(options.SshConnection,
                    options.BaseUri.Host, options.BaseUri.Port, token).ConfigureAwait(false);
            if (options.BaseUri.Scheme == "https" && options.Value("trust", "system") == "custom")
            {
                _roots.ImportFromPemFile(options.Value("ca"));
                if (_roots.Count == 0) throw Text.Exception("RabbitMqCertificateInvalid");
                var policy = new X509ChainPolicy
                {
                    TrustMode = X509ChainTrustMode.CustomRootTrust,
                    RevocationMode = X509RevocationMode.NoCheck
                };
                policy.CustomTrustStore.AddRange(_roots);
                // 仅更换信任根，SslStream 仍验证主机名，SSH 仍使用原始主机的 SNI。
                handler.SslOptions = new SslClientAuthenticationOptions { CertificateChainPolicy = policy };
            }
            _invoker = new(handler);
        }
        catch { handler.Dispose(); foreach (var root in _roots) root.Dispose(); throw Text.Exception("RabbitMqCertificateInvalid"); }
    }
    internal Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => _invoker.SendAsync(request, token);
    public void Dispose() { _invoker.Dispose(); foreach (var root in _roots) root.Dispose(); }
}
