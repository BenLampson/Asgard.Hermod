namespace Asgard.Hermod.Sync.Tests;

/// <summary>每次操作使用独立的真实 API 测试客户端。</summary>
internal sealed class SyncTestClientFactory(ApiTestHost host) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => host.CreateClient();
}
