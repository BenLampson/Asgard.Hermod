using System.Globalization;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证完整 TOS 地址、旧配置及桶区域发现使用真实 SDK 的签名与寻址。</summary>
public sealed class TosEndpointTests
{
    /// <summary>普通和 S3 地址均无需用户提取区域，省略协议时默认 HTTPS。</summary>
    [Theory]
    [InlineData("https://tos-cn-shanghai.volces.com", null, "https", "test-bucket.tos-s3-cn-shanghai.volces.com", 443, "cn-shanghai")]
    [InlineData("https://tos-s3-cn-guangzhou.volces.com/", null, "https", "test-bucket.tos-s3-cn-guangzhou.volces.com", 443, "cn-guangzhou")]
    [InlineData("tos-cn-beijing.volces.com", null, "https", "test-bucket.tos-s3-cn-beijing.volces.com", 443, "cn-beijing")]
    [InlineData("tos-s3-ap-southeast-1.volces.com", null, "https", "test-bucket.tos-s3-ap-southeast-1.volces.com", 443, "ap-southeast-1")]
    [InlineData("https://tos-cn-shanghai.ivolces.com", null, "https", "test-bucket.tos-s3-cn-shanghai.ivolces.com", 443, "cn-shanghai")]
    [InlineData("http://tos-s3-cn-beijing.ivolces.com:8080", "cn-shanghai", "http", "test-bucket.tos-s3-cn-shanghai.ivolces.com", 8080, "cn-shanghai")]
    [InlineData(" HTTPS://TOS-CN-BEIJING.VOLCES.COM/ ", null, "https", "test-bucket.tos-s3-cn-beijing.volces.com", 443, "cn-beijing")]
    [InlineData("cn-shanghai", null, "https", "test-bucket.tos-s3-cn-shanghai.volces.com", 443, "cn-shanghai")]
    [InlineData("", null, "https", "test-bucket.tos-s3-cn-beijing.volces.com", 443, "cn-beijing")]
    public async Task ListObjects_DetectsRegionAndPreservesEndpointRoute(string value, string? discoveredRegion,
        string scheme, string host, int port, string signingRegion)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(scheme, request.RequestUri!.Scheme);
            Assert.Equal(host, request.RequestUri.Host);
            Assert.Equal(port, request.RequestUri.Port);
            Assert.Equal("/", request.RequestUri.AbsolutePath);
            Assert.Contains($"/{signingRegion}/s3/aws4_request", request.Headers.GetValues("Authorization").Single());
            return Task.FromResult(S3TestClient.Xml("<ListBucketResult/>"));
        });
        var options = S3TestClient.Options("volcengine-tos", ("region", value));
        using var client = S3TestClient.Provider(handler, "volcengine-tos").CreateClient(options, discoveredRegion);
        await client.ListObjectsAsync("test-bucket", "", null, "/", 100, S3TestClient.Token);
        Assert.Equal(1, calls);
        Assert.Equal(value, options.Parameters["region"]);
        var link = new Uri(client.CreateTemporaryLink("test-bucket", "file.txt", TimeSpan.FromMinutes(5)));
        Assert.Equal(host, link.Host);
        Assert.Equal(scheme, link.Scheme);
        Assert.Equal(port, link.Port);
    }

    /// <summary>错误地址在联网前拒绝；本地化提示不泄露用户输入的凭据。</summary>
    [Theory]
    [InlineData("https://user:private-secret@tos-cn-beijing.volces.com")]
    [InlineData("https://tos-cn-beijing.volces.com/path")]
    [InlineData("https://tos-cn-beijing.volces.com?secret=private-secret")]
    [InlineData("https://tos-cn-beijing.volces.com#private-secret")]
    [InlineData("ftp://tos-cn-beijing.volces.com")]
    [InlineData("https://console.volcengine.com")]
    [InlineData("https://bucket.tos-cn-beijing.volces.com")]
    [InlineData("https://tos-cn-beijing.volces.com.example.com")]
    [InlineData("https://tos-cn-beijing.volces.com\\path")]
    [InlineData("https://tos-s3-.volces.com")]
    public void CreateClient_RejectsInvalidEndpointWithSafeBilingualError(string value)
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("No network"));
        var provider = S3TestClient.Provider(handler, "volcengine-tos");
        var error = Assert.Throws<ObjectStorageException>(() =>
            provider.CreateClient(S3TestClient.Options("volcengine-tos", ("region", value)), null));
        Assert.Equal("InvalidEndpoint", error.ErrorCode);
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            foreach (string language in new[] { "en", "zh-Hans", "fr-FR" })
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
                string key = Assert.IsType<string>(error.Data["Hermod.ResourceKey"]);
                Assert.Equal(Text.Get(key), Text.Error(error));
                Assert.DoesNotContain("private-secret", Text.Error(error));
                Assert.NotEqual(key, Text.Error(error));
            }
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }
}
