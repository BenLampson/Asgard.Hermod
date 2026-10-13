using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Asgard.Hermod.Redis.Tests;

/// <summary>临时 CA 签发 localhost 证书；本机 TLS 边界转发到显式启用的隔离 Redis。</summary>
internal sealed class RedisTlsTestBridge : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly X509Certificate2 _server;
    private readonly Task _accept;
    private readonly List<Task> _clients = [];
    private readonly int _redisPort;
    internal System.Collections.Concurrent.ConcurrentQueue<string> Failures { get; } = new();
    private readonly bool _requireClient;
    private readonly X509Certificate2 _authority;
    internal string CertificatePath => AuthorityPath + ".client.pem";
    internal string PrivateKeyPath => AuthorityPath + ".client.key";
    internal bool ClientAuthenticated { get; private set; }
    internal string AuthorityPath { get; } = Path.Combine(Path.GetTempPath(), "hermod-test-ca-" + Guid.NewGuid().ToString("N") + ".pem");
    internal int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    internal RedisTlsTestBridge(int redisPort, bool requireClient = false)
    {
        _redisPort = redisPort;
        _requireClient = requireClient;
        using var rootKey = RSA.Create(2048); using var serverKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest("CN=Hermod isolated test CA", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
        using var root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(1));
        File.WriteAllText(AuthorityPath, root.ExportCertificatePem());
        _authority = X509Certificate2.CreateFromPem(root.ExportCertificatePem());
        var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, true));
        using var issued = request.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        using var keyed = issued.CopyWithPrivateKey(serverKey);
        _server = X509CertificateLoader.LoadPkcs12(keyed.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.UserKeySet);
        using var clientKey = RSA.Create(2048);
        var clientRequest = new CertificateRequest("CN=Hermod test client", clientKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        clientRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.2") }, true));
        using var clientIssued = clientRequest.Create(root, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(30), RandomNumberGenerator.GetBytes(16));
        File.WriteAllText(CertificatePath, clientIssued.ExportCertificatePem());
        File.WriteAllText(PrivateKeyPath, clientKey.ExportPkcs8PrivateKeyPem());
        _listener.Start(); _accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try { while (true) _clients.Add(RelayAsync(await _listener.AcceptTcpClientAsync(_stop.Token))); }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }
    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var upstream = new TcpClient())
        using (var input = new SslStream(client.GetStream()))
        {
            try
            {
                await input.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _server,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    ClientCertificateRequired = _requireClient,
                    RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    {
                        if (!_requireClient) return true;
                        if (certificate is null) return false;
                        using var chain = new X509Chain(); chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.CustomTrustStore.Add(_authority); chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.ApplicationPolicy.Add(new("1.3.6.1.5.5.7.3.2"));
                        using var presented = new X509Certificate2(certificate);
                        return ClientAuthenticated = chain.Build(presented);
                    }
                }, _stop.Token);
                await upstream.ConnectAsync(IPAddress.Loopback, _redisPort, _stop.Token);
                using var relay = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                Task send = input.CopyToAsync(upstream.GetStream(), relay.Token);
                Task receive = upstream.GetStream().CopyToAsync(input, relay.Token);
                await Task.WhenAny(send, receive); relay.Cancel();
                try { await Task.WhenAll(send, receive); } catch (OperationCanceledException) { }
            }
            catch (AuthenticationException error) { Failures.Enqueue(error.ToString()); }
            catch (IOException) { }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        _stop.Cancel(); await _accept; _listener.Stop(); await Task.WhenAll(_clients);
        _server.Dispose(); _authority.Dispose(); _stop.Dispose(); File.Delete(AuthorityPath); File.Delete(CertificatePath); File.Delete(PrivateKeyPath);
    }
}
