using Amazon.Runtime;

namespace Asgard.Hermod.Providers.S3;

/// <summary>让 AWS SDK 使用应用管理的连接池；不复用 SDK 的跨实例凭据客户端缓存。</summary>
internal sealed class S3HttpClientFactory(IHttpClientFactory clients, S3BucketListDialect? dialect = null) : HttpClientFactory
{
    /// <inheritdoc />
    public override HttpClient CreateHttpClient(IClientConfig clientConfig)
    {
        var transport = clients.CreateClient(S3ServiceCollectionExtensions.HttpClientName);
        return dialect is null ? transport : new HttpClient(new S3BucketListResponseHandler(transport, dialect))
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
    /// <inheritdoc />
    public override bool UseSDKHttpClientCaching(IClientConfig clientConfig) => false;
    /// <inheritdoc />
    public override bool DisposeHttpClientsAfterUse(IClientConfig clientConfig) => true;
}
