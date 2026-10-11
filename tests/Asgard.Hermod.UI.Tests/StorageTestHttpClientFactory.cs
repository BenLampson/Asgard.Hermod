using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>提供独立客户端包装，共享由测试负责释放的 HTTP 替身。</summary>
public sealed class StorageTestHttpClientFactory : IHttpClientFactory
{
    /// <summary>测试边界上的请求处理器。</summary>
    private readonly HttpMessageHandler _handler;

    /// <summary>保留处理器归测试所有，避免一次连接测试提前释放共享替身。</summary>
    public StorageTestHttpClientFactory(HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    /// <inheritdoc />
    public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false)
    {
        Timeout = Timeout.InfiniteTimeSpan,
    };
}
