using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Asgard.Hermod.MySql;

/// <summary>SSH 的本地端口不改变数据库证书应验证的逻辑主机名。</summary>
internal static class MySqlTls
{
    internal static void Configure(MySqlConnection connection, MySqlSessionOptions options, List<IDisposable> owned)
    {
        if (options.TlsMode == "disabled") return;
        if (options.Value("clientCertificate", "no") == "yes")
        {
            var client = X509CertificateLoader.LoadPkcs12FromFile(options.Value("certificate"), options.Value("certificatePassword"),
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            if (!client.HasPrivateKey) { client.Dispose(); throw new MySqlFailure("MySqlCertificateInvalid", "MYSQL_CERTIFICATE"); }
            owned.Add(client); connection.ProvideClientCertificatesCallback = certificates => { certificates.Add(client); return ValueTask.CompletedTask; };
        }
        if (options.TlsMode != "verify-full") return;
        var authorities = new X509Certificate2Collection();
        if (options.Value("trust", "system") == "custom")
        {
            authorities.ImportFromPemFile(options.Value("ca"));
            if (authorities.Count == 0) throw new MySqlFailure("MySqlCertificateInvalid", "MYSQL_CERTIFICATE");
            owned.AddRange(authorities.Cast<IDisposable>());
        }
        connection.RemoteCertificateValidationCallback = (_, certificate, suppliedChain, _) =>
        {
            if (certificate is null) return false;
            using var server = new X509Certificate2(certificate);
            if (!server.MatchesHostname(options.Host, allowWildcards: true, allowCommonName: true)) return false;
            using var chain = new X509Chain();
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            if (suppliedChain is not null)
                foreach (var element in suppliedChain.ChainElements) chain.ChainPolicy.ExtraStore.Add(element.Certificate);
            if (authorities.Count > 0)
            {
                chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                chain.ChainPolicy.CustomTrustStore.AddRange(authorities);
                chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            }
            else chain.ChainPolicy.RevocationMode = X509RevocationMode.Online;
            return chain.Build(server);
        };
    }
}
