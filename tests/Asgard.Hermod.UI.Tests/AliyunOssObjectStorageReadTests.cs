using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>以真实官方 SDK 验证列表分页、容器区域、元数据、权限和只读临时链接。</summary>
public sealed class AliyunOssObjectStorageReadTests
{
    /// <summary>账户列表遍历 Marker，并从 Region 或历史 Location 恢复每个容器自己的区域。</summary>
    [Fact]
    public async Task ListBucketsAsync_ReadsAllPagesAndNormalizesReturnedRegions()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("oss-cn-hangzhou.aliyuncs.com", request.RequestUri?.Host);
            Assert.Contains("max-keys=100", request.RequestUri?.Query);
            Assert.DoesNotContain("resource-private-secret", request.RequestUri?.AbsoluteUri ?? "");
            string authorization = Assert.Single(request.Headers.GetValues("Authorization"));
            Assert.Contains("OSS4-HMAC-SHA256", authorization);
            string xml = calls == 1
                ? "<ListAllMyBucketsResult><IsTruncated>true</IsTruncated><NextMarker>first-bucket</NextMarker>" +
                  "<Buckets><Bucket><Name>first-bucket</Name><Location>oss-cn-beijing</Location><StorageClass>Standard</StorageClass>" +
                  "</Bucket></Buckets></ListAllMyBucketsResult>"
                : "<ListAllMyBucketsResult><IsTruncated>false</IsTruncated><Buckets><Bucket><Name>second-bucket</Name>" +
                  "<Region>us-west-1</Region><StorageClass>IA</StorageClass></Bucket></Buckets></ListAllMyBucketsResult>";
            if (calls == 2) Assert.Contains("marker=first-bucket", request.RequestUri?.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml) });
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        IReadOnlyList<ObjectStorageBucket> buckets = await client.ListBucketsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, calls);
        Assert.Equal(["cn-beijing", "us-west-1"], buckets.Select(bucket => bucket.Region));
        Assert.Equal("IA", buckets[1].StorageClass);
    }

    /// <summary>区域来自容器，不允许将阿里云凭据传到任意外部主机。</summary>
    [Fact]
    public void ResourceProvider_RejectsInvalidRegionAndMismatchedTypeBeforeHttp()
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("无效配置不能请求网络。"));
        Assert.Throws<ObjectStorageException>(() => AliyunOssResourceTestClient.Create(handler, "cn-hangzhou.attacker.test"));
        Assert.Throws<ObjectStorageException>(() => AliyunOssResourceTestClient.Create(handler, "../../attacker"));
        var provider = new Asgard.Hermod.Infrastructure.Storage.AliyunOssObjectStorageProvider(new StorageTestHttpClientFactory(handler));
        ObjectStorageException error = Assert.Throws<ObjectStorageException>(() => provider.CreateClient(new InstanceConnectionOptions
        {
            TypeId = "docker",
            ProviderId = "aliyun-oss",
            Parameters = AliyunOssResourceTestClient.Options().Parameters,
        }, "cn-hangzhou"));
        Assert.Equal("ProviderMismatch", error.ErrorCode);
    }

    /// <summary>对象分页保留游标，目录只用 delimiter=/ 的 CommonPrefixes 形成。</summary>
    [Fact]
    public async Task ListObjectsAsync_UsesDelimiterAndContinuationToken()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal("test-bucket.oss-cn-beijing.aliyuncs.com", request.RequestUri?.Host);
            string query = Uri.UnescapeDataString(request.RequestUri?.Query ?? "");
            Assert.Contains("prefix=docs/", query);
            Assert.Contains("delimiter=/", query);
            Assert.Contains("max-keys=100", query);
            Assert.Contains("continuation-token=initial-token", query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<ListBucketResult><Name>test-bucket</Name><Prefix>docs/</Prefix><IsTruncated>true</IsTruncated>" +
                    "<NextContinuationToken>next-token</NextContinuationToken><CommonPrefixes><Prefix>docs/sub/</Prefix></CommonPrefixes>" +
                    "<Contents><Key>docs/readme.txt</Key><Size>42</Size><LastModified>2026-10-12T00:00:00.000Z</LastModified>" +
                    "<StorageClass>Standard</StorageClass></Contents></ListBucketResult>"),
            });
        });
        using var client = AliyunOssResourceTestClient.Create(handler, "cn-beijing");

        ObjectStoragePage page = await client.ListObjectsAsync("test-bucket", "docs/", "initial-token", "/", 100,
            TestContext.Current.CancellationToken);

        Assert.Equal("next-token", page.NextToken);
        Assert.Equal("docs/sub/", page.Entries[0].Key);
        Assert.True(page.Entries[0].IsDirectory);
        Assert.Equal(42, page.Entries[1].Size);
        Assert.NotNull(page.Entries[1].LastModified);
    }

    /// <summary>元数据来自 HEAD，权限来自 GetObjectACL，不下载正文或猜测公开权限。</summary>
    [Fact]
    public async Task GetObjectMetadataAsync_ReadsHeadAndActualAcl()
    {
        var methods = new List<HttpMethod>();
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            methods.Add(request.Method);
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            if (request.Method == HttpMethod.Head)
            {
                response.Content = new ByteArrayContent([]);
                response.Content.Headers.ContentLength = 42;
                response.Content.Headers.ContentType = new("text/plain");
                response.Content.Headers.LastModified = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
                response.Headers.Add("x-oss-storage-class", "IA");
            }
            else
            {
                Assert.Contains("acl", request.RequestUri?.Query);
                response.Content = new StringContent("<AccessControlPolicy><AccessControlList><Grant>private</Grant>" +
                    "</AccessControlList></AccessControlPolicy>");
            }
            return Task.FromResult(response);
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        ObjectStorageObjectDetails details = await client.GetObjectMetadataAsync("test-bucket", "README.txt", TestContext.Current.CancellationToken);

        Assert.Equal([HttpMethod.Head, HttpMethod.Get], methods);
        Assert.Equal(42, details.Size);
        Assert.Equal("IA", details.StorageClass);
        Assert.Equal("text/plain", details.ContentType);
        Assert.Equal("private", details.AccessControl);
    }

    /// <summary>缺少 ACL 查询权限时，已成功读取的元数据仍可展示且不猜测公开权限。</summary>
    [Fact]
    public async Task GetObjectMetadataAsync_PreservesHeadWhenAclPermissionIsMissing()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Head)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
                response.Content.Headers.ContentLength = 42;
                response.Content.Headers.ContentType = new("text/plain");
                response.Headers.Add("x-oss-storage-class", "IA");
                return Task.FromResult(response);
            }
            Assert.Contains("acl", request.RequestUri?.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("<Error><Code>AccessDenied</Code>" +
                    "<Message>resource-private-secret LTAIResourceTestKey</Message></Error>"),
            });
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        ObjectStorageObjectDetails details = await client.GetObjectMetadataAsync("test-bucket", "README.txt", TestContext.Current.CancellationToken);

        Assert.Equal(42, details.Size);
        Assert.Equal("IA", details.StorageClass);
        Assert.Equal("text/plain", details.ContentType);
        Assert.Equal("读取权限未知", details.AccessControl);
        Assert.DoesNotContain("resource-private-secret", details.ToString());
    }

    /// <summary>ACL 的签名失败不属于权限降级，仍返回安全认证失败。</summary>
    [Fact]
    public async Task GetObjectMetadataAsync_DoesNotHideAclAuthenticationFailure()
    {
        using var handler = new StorageTestHttpHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Head
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) }
                : new HttpResponseMessage(HttpStatusCode.Forbidden)
                {
                    Content = new StringContent("<Error><Code>SignatureDoesNotMatch</Code>" +
                        "<Message>resource-private-secret LTAIResourceTestKey</Message></Error>"),
                }));
        using var client = AliyunOssResourceTestClient.Create(handler);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.GetObjectMetadataAsync(
            "test-bucket", "README.txt", TestContext.Current.CancellationToken));

        Assert.Equal("AuthenticationFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
    }

    /// <summary>不存在对象与权限失败必须不同，原始 OSS 消息及身份字段不能通过异常返回。</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "NoSuchKey", false)]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", true)]
    public async Task ObjectExistsAsync_DistinguishesMissingFromPermissionFailure(HttpStatusCode status, string code, bool fails)
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent($"<Error><Code>{code}</Code><Message>resource-private-secret LTAIResourceTestKey</Message></Error>"),
        }));
        using var client = AliyunOssResourceTestClient.Create(handler);
        Task<bool> operation = client.ObjectExistsAsync("test-bucket", "file.txt", TestContext.Current.CancellationToken);
        if (!fails) Assert.False(await operation);
        else
        {
            ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => operation);
            Assert.Equal("AccessDenied", error.ErrorCode);
            Assert.Null(error.InnerException);
            Assert.DoesNotContain("resource-private-secret", error.ToString());
            Assert.DoesNotContain("LTAIResourceTestKey", error.ToString());
        }
    }

    /// <summary>预签是本地只读 GET 链接，包含短有效期，密钥 Secret 不进入 URL。</summary>
    [Theory]
    [InlineData(15)]
    [InlineData(60)]
    public void CreateTemporaryLink_UsesShortGetSignatureWithoutHttp(int minutes)
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("预签不需要网络。"));
        using var client = AliyunOssResourceTestClient.Create(handler, "cn-beijing");

        var url = new Uri(client.CreateTemporaryLink("test-bucket", "docs/file.txt", TimeSpan.FromMinutes(minutes)));

        Assert.Equal("https", url.Scheme);
        Assert.Equal("test-bucket.oss-cn-beijing.aliyuncs.com", url.Host);
        Assert.Equal("/docs/file.txt", url.AbsolutePath);
        Assert.Contains("x-oss-signature-version=OSS4-HMAC-SHA256", url.Query);
        Dictionary<string, string> query = url.Query.TrimStart('?').Split('&')
            .Select(parameter => parameter.Split('=', 2))
            .ToDictionary(parameter => parameter[0], parameter => Uri.UnescapeDataString(parameter[1]), StringComparer.Ordinal);
        // SDK 从调用后的签名时刻计算剩余整秒，舍去不足一秒的部分。
        Assert.InRange(int.Parse(query["x-oss-expires"], System.Globalization.CultureInfo.InvariantCulture),
            minutes * 60 - 1, minutes * 60);
        Assert.Matches("^[0-9a-f]{64}$", query["x-oss-signature"]);
        Assert.DoesNotContain("resource-private-secret", url.AbsoluteUri);
    }

    /// <summary>范围 GET 在网络层限制预览长度，正文由调用方释放。</summary>
    [Fact]
    public async Task OpenReadAsync_UsesBoundedRangeForPreview()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal("bytes=0-262143", request.Headers.Range?.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StringContent("small preview") };
            response.Content.Headers.ContentLength = 13;
            response.Content.Headers.ContentRange = new(0, 12, 13);
            return Task.FromResult(response);
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        await using var response = await client.OpenReadAsync("test-bucket", "file.txt", 256 * 1024, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(response.Body, leaveOpen: true);

        Assert.Equal("small preview", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
        Assert.Equal(13, response.Size);
    }
}
