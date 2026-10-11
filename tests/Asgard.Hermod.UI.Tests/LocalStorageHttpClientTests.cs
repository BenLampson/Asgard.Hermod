using System.Net;
using System.Net.Http;
using Asgard.Hermod.Infrastructure.Storage;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实依赖注入与 HTTP 弹性装配，避免 SDK 替身掩盖实际连接的策略错误。</summary>
public sealed class LocalStorageHttpClientTests
{
    /// <summary>真实命名客户端和 SDK 连接器能够完成策略验证，构造过程不执行网络请求。</summary>
    [Fact]
    public void AddLocalStorage_CreatesValidatedHttpAndResourceClients()
    {
        using var environment = new LocalStorageTestEnvironment();
        using ServiceProvider services = environment.CreateServices();
        IHttpClientFactory http = services.GetRequiredService<IHttpClientFactory>();

        using HttpClient connection = http.CreateClient(AliyunOssStorageProvider.HttpClientName);
        using HttpClient resources = http.CreateClient(AliyunOssObjectStorageProvider.HttpClientName);
        using IObjectStorageClient client = services.GetRequiredService<IObjectStorageClientFactory>()
            .CreateClient(AliyunOssResourceTestClient.Options(), "cn-beijing");

        Assert.Equal(Timeout.InfiniteTimeSpan, connection.Timeout);
        Assert.Equal(Timeout.InfiniteTimeSpan, resources.Timeout);
    }

    /// <summary>生产 SDK 和真实 HTTP 弹性策略都不重试 PUT，远程写入失败只发送一次。</summary>
    [Fact]
    public async Task UploadAsync_DoesNotRetryWritesThroughRealHttpFactory()
    {
        using var environment = new LocalStorageTestEnvironment();
        int requests = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Put, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<Error><Code>ServiceUnavailable</Code>" +
                    "<Message>resource-private-secret LTAIResourceTestKey</Message></Error>"),
            });
        });
        var registration = new ServiceCollection().AddLocalStorage(environment.CreateConfiguration());
        registration.AddHttpClient(AliyunOssObjectStorageProvider.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        using ServiceProvider services = registration.BuildServiceProvider();
        using IObjectStorageClient client = services.GetRequiredService<IObjectStorageClientFactory>()
            .CreateClient(AliyunOssResourceTestClient.Options(), "cn-hangzhou");
        using var body = new MemoryStream([1, 2, 3]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadAsync(
            "test-bucket", "file.bin", body, 3, overwrite: false, progress: null, TestContext.Current.CancellationToken));

        Assert.Equal(1, requests);
        Assert.Equal("ServiceUnavailable", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
    }
}
