using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Asgard.Hermod.PostgreSql;

/// <summary>SSH 回环地址不改变数据库证书应验证的逻辑主机名。</summary>
internal static class PostgreSqlTls
{
    internal static void Configure(NpgsqlDataSourceBuilder builder, PostgreSqlSessionOptions options, List<IDisposable> owned)
    {
        if (options.TlsMode == "disabled") return;
        X509Certificate2? client = null;
        if (options.Value("clientCertificate", "no") == "yes")
        {
            client = X509CertificateLoader.LoadPkcs12FromFile(options.Value("certificate"), options.Value("certificatePassword"),
                OperatingSystem.IsWindows() ? X509KeyStorageFlags.UserKeySet : X509KeyStorageFlags.EphemeralKeySet);
            if (!client.HasPrivateKey) { client.Dispose(); throw new PostgreSqlFailure("PostgreSqlCertificateInvalid", "POSTGRESQL_CERTIFICATE"); }
            owned.Add(client);
        }
        var authorities = new X509Certificate2Collection();
        if (options.TlsMode == "verify-full" && options.Value("trust", "system") == "custom")
        {
            authorities.ImportFromPemFile(options.Value("ca"));
            if (authorities.Count == 0) throw new PostgreSqlFailure("PostgreSqlCertificateInvalid", "POSTGRESQL_CERTIFICATE");
            owned.AddRange(authorities.Cast<IDisposable>());
        }
        builder.UseSslClientAuthenticationOptionsCallback(ssl =>
        {
            ssl.TargetHost = options.Host;
            if (client is not null) ssl.ClientCertificates = new X509CertificateCollection { client };
            if (options.TlsMode != "verify-full") return;
            ssl.RemoteCertificateValidationCallback = (_, certificate, suppliedChain, _) =>
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
        });
    }
}
