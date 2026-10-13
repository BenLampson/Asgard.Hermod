using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Asgard.Hermod.Redis;

/// <summary>自定义信任仍核对主机名；绝不提供跳过证书验证的选项。</summary>
internal static class RedisTls
{
    internal static void Configure(ConfigurationOptions configuration, RedisSessionOptions options, string prefix, List<IDisposable> owned)
    {
        if (options.Value(prefix + "tls", "no") != "yes") return;
        configuration.Ssl = true;
        X509Certificate2? certificate = null;
        var authorities = new X509Certificate2Collection();
        if (options.Value(prefix + "trust", "system") == "custom")
        {
            authorities.ImportFromPemFile(options.Value(prefix + "ca"));
            if (authorities.Count == 0) throw Text.Exception("RedisCertificateInvalid");
            owned.AddRange(authorities.Cast<IDisposable>());
        }
        if (options.Value(prefix + "mtls", "no") == "yes")
        {
            certificate = options.Value(prefix + "certificatePassword").Length == 0
                ? X509Certificate2.CreateFromPemFile(options.Value(prefix + "certificate"), options.Value(prefix + "privateKey"))
                : X509Certificate2.CreateFromEncryptedPemFile(options.Value(prefix + "certificate"), options.Value(prefix + "certificatePassword"), options.Value(prefix + "privateKey"));
            // Windows SslStream 需要持久的临时 PFX 句柄，私钥不落盘。
            var portable = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pfx), null, X509KeyStorageFlags.EphemeralKeySet);
            certificate.Dispose(); certificate = portable; owned.Add(certificate);
        }
        configuration.SslClientAuthenticationOptions = host => new SslClientAuthenticationOptions
        {
            TargetHost = options.Value(prefix + "tls.host") is { Length: > 0 } name ? name : host,
            ClientCertificates = certificate is null ? null : new X509CertificateCollection { certificate },
            CertificateChainPolicy = authorities.Count == 0 ? null : new X509ChainPolicy
            {
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { }, RevocationMode = X509RevocationMode.Online,
            },
        };
        if (authorities.Count != 0)
        {
            var create = configuration.SslClientAuthenticationOptions;
            configuration.SslClientAuthenticationOptions = host =>
            {
                var settings = create(host); settings.CertificateChainPolicy!.CustomTrustStore.AddRange(authorities); return settings;
            };
        }
    }
}
