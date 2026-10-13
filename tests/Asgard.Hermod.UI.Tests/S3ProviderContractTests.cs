using System.Net;
using System.Net.Http;
using Asgard.Hermod.Abstractions.Localization;

namespace Asgard.Hermod.UI.Tests;

/// <summary>覆盖各连接器的端点、签名、分页、错误和临时凭据契约。</summary>
public sealed class S3ProviderContractTests
{
    /// <summary>各服务商真实 SDK 发出 SigV4 请求，自建服务使用路径寻址。</summary>
    [Theory]
    [InlineData("aws-s3", "us-east-1", "test-bucket.s3.us-east-1.amazonaws.com", "/")]
    [InlineData("aws-s3", "cn-north-1", "test-bucket.s3.cn-north-1.amazonaws.com.cn", "/")]
    [InlineData("tencent-cos", "ap-guangzhou", "test-bucket.cos.ap-guangzhou.myqcloud.com", "/")]
    [InlineData("volcengine-tos", "cn-beijing", "test-bucket.tos-s3-cn-beijing.volces.com", "/")]
    [InlineData("minio", "us-east-1", "localhost", "/test-bucket")]
    [InlineData("s3-compatible", "us-east-1", "localhost", "/test-bucket")]
    public async Task ListObjects_UsesProviderEndpointAndSignedTemporaryCredentials(string provider, string region, string host, string path)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.Equal(path, request.RequestUri.AbsolutePath.TrimEnd('/') is "" ? "/" : request.RequestUri.AbsolutePath.TrimEnd('/'));
            string signature = request.Headers.GetValues("Authorization").Single();
            Assert.StartsWith("AWS4-HMAC-SHA256", signature);
            Assert.Contains($"/{region}/s3/aws4_request", signature);
            Assert.Equal("temporary-token", request.Headers.GetValues("x-amz-security-token").Single());
            Assert.Contains("list-type=2", request.RequestUri.Query);
            return Task.FromResult(S3TestClient.Xml("<ListBucketResult><IsTruncated>false</IsTruncated><Contents><Key>目录/a.txt</Key><Size>7</Size><LastModified>2026-01-01T00:00:00Z</LastModified><StorageClass>STANDARD</StorageClass></Contents><CommonPrefixes><Prefix>目录/子目录/</Prefix></CommonPrefixes></ListBucketResult>"));
        });
        using var client = S3TestClient.Provider(handler, provider).CreateClient(
            S3TestClient.Options(provider, ("sessionToken", "temporary-token")), region);
        var result = await client.ListObjectsAsync("test-bucket", "目录/", null, "/", 1000, S3TestClient.Token);
        Assert.Equal(1, calls);
        Assert.Equal(2, result.Entries.Count);
        Assert.True(result.Entries[0].IsDirectory);
        Assert.Equal("目录/a.txt", result.Entries[1].Key);
        Assert.Equal(7, result.Entries[1].Size);
    }

    /// <summary>无效参数在任何网络请求之前拒绝，不接受 URL 内嵌凭据或任意区域字符。</summary>
    [Theory]
    [InlineData("endpoint", "https://user:password@example.com", "InvalidEndpoint")]
    [InlineData("endpoint", "https://example.com/path", "InvalidEndpoint")]
    [InlineData("endpoint", "https://example.com?secret=key", "InvalidEndpoint")]
    [InlineData("endpoint", "file:///tmp", "InvalidEndpoint")]
    [InlineData("region", "cn-beijing/../evil", "InvalidRegion")]
    [InlineData("bucket", "../bucket", "InvalidBucket")]
    [InlineData("accessKeyId", "", "InvalidCredentials")]
    [InlineData("addressingStyle", "invalid", "InvalidAddressingStyle")]
    public void Configuration_RejectsInvalidValuesBeforeNetwork(string key, string value, string expected)
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("Network must not be reached"));
        var provider = S3TestClient.Provider(handler, "minio");
        var error = Assert.Throws<ObjectStorageException>(() => provider.CreateClient(S3TestClient.Options("minio", (key, value)), null));
        Assert.Equal(expected, error.ErrorCode);
    }

    /// <summary>受限账户指定桶后无需 ListAllMyBuckets 权限。</summary>
    [Fact]
    public async Task FixedBucket_ListsOnlyConfiguredBucket()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal("test-bucket.s3.us-east-1.amazonaws.com", request.RequestUri!.Host);
            Assert.Contains("list-type=2", request.RequestUri.Query);
            return Task.FromResult(S3TestClient.Xml("<ListBucketResult/>"));
        });
        using var client = S3TestClient.Provider(handler, "aws-s3").CreateClient(S3TestClient.Options("aws-s3", ("bucket", "test-bucket")), null);
        var bucket = Assert.Single(await client.ListBucketsAsync(S3TestClient.Token));
        Assert.Equal("test-bucket", bucket.Name);
        Assert.Equal("us-east-1", bucket.Region);
    }

    /// <summary>COS 使用 marker/NextMarker 和 Location，不能按 AWS 字段遗漏后续容器。</summary>
    [Fact]
    public async Task TencentBuckets_AdaptsNativePaginationBeforeSigning()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("cos.ap-guangzhou.myqcloud.com", request.RequestUri!.Host);
            Assert.Contains("max-keys=1000", request.RequestUri.Query);
            Assert.DoesNotContain("max-buckets", request.RequestUri.Query);
            if (calls == 2)
            {
                Assert.Contains("marker=bucket-1", request.RequestUri.Query);
                Assert.DoesNotContain("continuation-token", request.RequestUri.Query);
            }
            return Task.FromResult(S3TestClient.Xml($"<ListAllMyBucketsResult><Buckets><Bucket><Name>bucket-{calls}</Name><Location>ap-guangzhou</Location></Bucket></Buckets>{(calls == 1 ? "<NextMarker>bucket-1</NextMarker>" : "")}</ListAllMyBucketsResult>"));
        });
        using var client = S3TestClient.Provider(handler, "tencent-cos").CreateClient(S3TestClient.Options("tencent-cos"), null);
        var buckets = await client.ListBucketsAsync(S3TestClient.Token);
        Assert.Equal(2, calls);
        Assert.Equal(2, buckets.Count);
        Assert.All(buckets, bucket => Assert.Equal("ap-guangzhou", bucket.Region));
    }

    /// <summary>列桶遍历不透明游标，区域来自响应；错误循环游标必须失败。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Buckets_TraversesPagesAndRejectsRepeatedCursor(bool repeat)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            if (calls > 1) Assert.Contains("continuation-token=next", request.RequestUri!.Query);
            return Task.FromResult(S3TestClient.Xml($"<ListAllMyBucketsResult><Buckets><Bucket><Name>bucket-{calls}</Name><BucketRegion>eu-west-1</BucketRegion></Bucket></Buckets>{(calls == 1 || repeat ? "<ContinuationToken>next</ContinuationToken>" : "")}</ListAllMyBucketsResult>"));
        });
        using var client = S3TestClient.Provider(handler, "aws-s3").CreateClient(S3TestClient.Options(), null);
        if (repeat)
        {
            var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.ListBucketsAsync(S3TestClient.Token));
            Assert.Equal("InvalidResponse", error.ErrorCode);
        }
        else
        {
            var buckets = await client.ListBucketsAsync(S3TestClient.Token);
            Assert.Equal(2, buckets.Count);
            Assert.All(buckets, bucket => Assert.Equal("eu-west-1", bucket.Region));
        }
        Assert.Equal(2, calls);
    }

    /// <summary>只有明确的 HEAD 404 表示对象不存在，403 不能启动覆盖或清理。</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Forbidden, true)]
    public async Task Exists_DistinguishesMissingFromAccessDenied(HttpStatusCode status, bool throws)
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        using var client = S3TestClient.Provider(handler, "minio").CreateClient(S3TestClient.Options("minio"), null);
        if (throws)
        {
            var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.ObjectExistsAsync("test-bucket", "a.txt", S3TestClient.Token));
            Assert.Equal("AccessDenied", error.ErrorCode);
        }
        else Assert.False(await client.ObjectExistsAsync("test-bucket", "a.txt", S3TestClient.Token));
    }

    /// <summary>错误响应包含敏感内容时，不传播服务端文本及 SDK 内部异常。</summary>
    [Fact]
    public async Task Errors_DoNotExposeSdkMessages()
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(S3TestClient.Xml(
            "<Error><Code>SignatureDoesNotMatch</Code><Message>sdk-private-test-secret</Message></Error>", HttpStatusCode.Forbidden)));
        using var client = S3TestClient.Provider(handler, "aws-s3").CreateClient(S3TestClient.Options(), null);
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.ListObjectsAsync("test-bucket", "", null, "/", 100, S3TestClient.Token));
        Assert.Equal("AuthenticationFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("sdk-private-test-secret", Text.Error(error));
    }

    /// <summary>签名链接由 SDK 编码 Unicode 键并携带临时令牌，且不会发送请求。</summary>
    [Fact]
    public void TemporaryLink_PreservesKeyTokenAndExpiryLimit()
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("No network"));
        using var client = S3TestClient.Provider(handler, "minio").CreateClient(S3TestClient.Options("minio", ("sessionToken", "test/token+value")), null);
        var uri = new Uri(client.CreateTemporaryLink("test-bucket", "目录/a #+.txt", TimeSpan.FromMinutes(5)));
        Assert.Equal("/test-bucket/目录/a #+.txt", Uri.UnescapeDataString(uri.AbsolutePath));
        Assert.Contains("X-Amz-Security-Token=test%2Ftoken%2Bvalue", uri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("X-Amz-Expires=300", uri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sdk-private-test-secret", uri.AbsoluteUri);
        Assert.Throws<ObjectStorageException>(() => client.CreateTemporaryLink("test-bucket", "key", TimeSpan.FromDays(8)));
    }
}
