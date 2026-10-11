using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在 HTTP 边界注入确定性响应，验证实际请求而不访问第三方网络。</summary>
public sealed class StorageTestHttpHandler : HttpMessageHandler
{
    /// <summary>测试定义的只读请求响应。</summary>
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _response;

    /// <summary>请求行为由具体测试提供，取消标记原样透传。</summary>
    public StorageTestHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> response)
    {
        ArgumentNullException.ThrowIfNull(response);
        _response = response;
    }

    /// <summary>不修改请求，允许测试检查签名、URL 和取消标记。</summary>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        _response(request, cancellationToken);
}
