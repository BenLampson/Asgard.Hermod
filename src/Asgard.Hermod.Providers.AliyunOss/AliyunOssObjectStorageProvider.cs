using System.Net.Http;
using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>将本地密文配置恢复出的凭据交给官方 SDK，端点始终属于合法 OSS 服务域。</summary>
public sealed class AliyunOssObjectStorageProvider(IHttpClientFactory clients) : IObjectStorageProvider
{
    /// <summary>资源传输使用独立客户端，读取与写入遵循各自重试策略。</summary>
    public const string HttpClientName = "Hermod.AliyunOss.Resources";

    /// <inheritdoc />
    public string TypeId => AliyunOssStorageProvider.TypeId;
    /// <inheritdoc />
    public string ProviderId => AliyunOssStorageProvider.ProviderId;

    /// <inheritdoc />
    public IObjectStorageClient CreateClient(InstanceConnectionOptions options, string? region)
    {
        if (options.TypeId != TypeId || options.ProviderId != ProviderId)
            throw ObjectStorageException.FromResource("TheConnectionConfigurationDoesNotMatchTheSelectedInstanceTypeOrProvider", "ProviderMismatch");
        if (!options.Parameters.TryGetValue("accessKeyId", out string? accessKeyId) ||
            !options.Parameters.TryGetValue("accessKeySecret", out string? accessKeySecret) ||
            string.IsNullOrWhiteSpace(accessKeyId) || accessKeyId.Length > 128 || !accessKeyId.All(char.IsAsciiLetterOrDigit) ||
            string.IsNullOrWhiteSpace(accessKeySecret) || accessKeySecret.Length > 256 || accessKeySecret.Any(char.IsControl))
            throw ObjectStorageException.FromResource("InvalidConnectionCredentialsEditTheInstanceConfiguration", "InvalidCredentials");
        var settings = AliyunOssEndpoint.Resolve(options, region);
        var transport = new Oss.Transport.HttpTransport(clients.CreateClient(HttpClientName));
        try
        {
            var configuration = new Oss.Configuration
            {
                Region = settings.Region,
                Endpoint = settings.Endpoint.AbsoluteUri,
                CredentialsProvider = new Oss.Credentials.StaticCredentialsProvider(accessKeyId, accessKeySecret),
                HttpTransport = transport,
                SignatureVersion = "v4",
                Retryer = new Oss.Retry.NopRetryer(),
                DisableClockSkewCorrection = true,
                EnabledRedirect = false,
                InsecureSkipVerify = false,
                ReadWriteTimeout = TimeSpan.FromHours(1),
            };
            return new AliyunOssObjectStorageClient(new Oss.Client(configuration));
        }
        catch
        {
            transport.Dispose();
            throw;
        }
    }
}
