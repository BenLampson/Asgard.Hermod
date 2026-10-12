using System.Net;
using System.Net.Http;
using Asgard.Hermod.Infrastructure.Storage;

namespace Asgard.Hermod.UI.Tests;

/// <summary>完整 SDK 边界验证临时对象读写、候选容器范围、版本清理和不可信失败脱敏。</summary>
public sealed class AliyunOssConnectionProbeTests
{
    /// <summary>只有对象写入、指定版本回读和精确版本清理全部完成才通过。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("version-test-1")]
    [InlineData("null")]
    public async Task TestConnectionAsync_VerifiesReadWriteAndExactVersionCleanup(string? versionId)
    {
        using var server = new OssConnectionProbeTestServer { VersionId = versionId };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccessful);
        Assert.Null(result.ErrorCode);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.Empty(server.Objects);
        Assert.Contains("test-bucket", result.Message);
        Assert.Contains("仅覆盖该容器", result.Message);
        Assert.NotNull(result.Checks);
        Assert.All(result.Checks, check => Assert.True(check.IsSuccessful));
        Assert.Equal(versionId, Assert.Single(server.Requests, request => request.Method == HttpMethod.Get).VersionId);
        Assert.Equal(versionId, Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Put);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
    }

    /// <summary>首个容器只读不会终止整个 Key 的验证，成功结果只说明后续实际通过的容器。</summary>
    [Fact]
    public async Task TestConnectionAsync_TriesWritableBucketAfterReadOnlyBucket()
    {
        using var server = new OssConnectionProbeTestServer
        {
            Buckets = [new("a-readonly-bucket", "cn-hangzhou"), new("z-writable-bucket", "cn-beijing")],
        };
        server.Upload = (bucket, key, payload, _) => Task.FromResult(bucket == "a-readonly-bucket"
            ? OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied")
            : server.PutSuccess(bucket, key, payload));

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccessful);
        Assert.Contains("z-writable-bucket", result.Message);
        Assert.DoesNotContain("a-readonly-bucket", result.Message);
        Assert.Equal(2, server.Requests.Count(request => request.Method == HttpMethod.Put));
        Assert.Equal(2, server.Requests.Where(request => request.Method == HttpMethod.Put).Select(request => request.Key).Distinct().Count());
        Assert.All(server.Requests.Where(request => request.Method == HttpMethod.Delete), request => Assert.Equal("z-writable-bucket", request.Bucket));
        Assert.Empty(server.Objects);
    }

    /// <summary>所有候选容器都拒绝写入时不得把身份或容器列表读取成功当作连接成功。</summary>
    [Fact]
    public async Task TestConnectionAsync_RejectsReadOnlyKeyWithoutFalseCleanup()
    {
        using var server = new OssConnectionProbeTestServer
        {
            Upload = (_, _, _, _) => Task.FromResult(OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied")),
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_write_denied", result.ErrorCode);
        Assert.DoesNotContain(server.Requests, request => request.Method == HttpMethod.Delete);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "身份认证" && check.IsSuccessful);
        Assert.DoesNotContain(result.Checks, check => check.Name == "临时对象清理");
        Assert.DoesNotContain("private-untrusted-response", result.ToString());
    }

    /// <summary>回读拒绝或错误正文均不通过，已经确认写入的版本仍必须清理。</summary>
    [Theory]
    [InlineData(true, "oss_probe_read_denied")]
    [InlineData(false, "oss_probe_content_mismatch")]
    public async Task TestConnectionAsync_CleansWrittenObjectAfterReadFailure(bool denied, string expectedCode)
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "written-version" };
        server.Read = (_, _, _, _) => Task.FromResult(denied
            ? OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) });

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.Empty(server.Objects);
        Assert.Equal("written-version", Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
    }

    /// <summary>写入响应丢失后先回读唯一正文确认归属，再清理回读得到的精确版本。</summary>
    [Fact]
    public async Task TestConnectionAsync_ResolvesLostUploadResponseBeforeCleanup()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "ambiguous-written-version" };
        server.Upload = (bucket, key, payload, _) =>
        {
            using HttpResponseMessage ignored = server.PutSuccess(bucket, key, payload);
            throw new HttpRequestException("resource-private-secret LTAIResourceTestKey raw upload response lost");
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_write_failed", result.ErrorCode);
        Assert.Empty(server.Objects);
        Assert.Null(Assert.Single(server.Requests, request => request.Method == HttpMethod.Get).VersionId);
        Assert.Equal("ambiguous-written-version", Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.DoesNotContain("raw upload response lost", result.ToString());
    }

    /// <summary>未知上传回读无法确认归属时，不删除对象也不伪造清理成功。</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.OK)]
    public async Task TestConnectionAsync_ReportsUnknownUploadWithoutBlindDeletion(HttpStatusCode readStatus)
    {
        using var server = new OssConnectionProbeTestServer
        {
            Upload = (_, _, _, _) => throw new HttpRequestException("resource-private-secret raw ambiguous upload"),
            Read = (_, _, _, _) => Task.FromResult(readStatus == HttpStatusCode.OK
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) }
                : OssConnectionProbeTestServer.Error(readStatus, readStatus == HttpStatusCode.NotFound ? "NoSuchKey" : "AccessDenied")),
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_cleanup_failed", result.ErrorCode);
        Assert.True(result.RequiresAttentionAfterCancellation);
        Assert.DoesNotContain(server.Requests, request => request.Method == HttpMethod.Delete);
        string key = Assert.Single(server.Requests, request => request.Method == HttpMethod.Put).Key;
        Assert.Contains("容器：test-bucket", result.Message);
        Assert.Contains("对象 Key：" + key, result.Message);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
    }

    /// <summary>当前客户端缺少版本清理能力时明确不支持，不回退到按键删除。</summary>
    [Fact]
    public async Task GenericProbe_RejectsClientWithoutVersionAwareCapability()
    {
        var client = new FakeObjectStorageClient
        {
            Upload = (_, _, _, _, _, _, _) => throw new InvalidOperationException("缺少探测能力不能写入。"),
            Delete = (_, _, _) => throw new InvalidOperationException("不能回退到普通按键删除。"),
        };
        var provider = new FakeObjectStorageProvider("storage", "unsupported", client);

        InstanceConnectionTestResult result = await new ObjectStorageConnectionProbe(provider).TestAsync(new()
        {
            TypeId = "storage",
            ProviderId = "unsupported",
            Parameters = new Dictionary<string, string>(),
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("storage_probe_unsupported", result.ErrorCode);
        Assert.False(result.RequiresAttentionAfterCancellation);
    }
}
