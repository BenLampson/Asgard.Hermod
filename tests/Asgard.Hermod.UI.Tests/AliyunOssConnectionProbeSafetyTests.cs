using System.Net;
using System.Net.Http;
using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证临时对象归属、已存在对象保护和精确清理失败的安全结果。</summary>
public sealed class AliyunOssConnectionProbeSafetyTests
{
    /// <summary>随机键预检发现旧对象时，不写入、不回读、不删除旧数据。</summary>
    [Fact]
    public async Task TestConnectionAsync_ProtectsObjectFoundDuringPreflight()
    {
        using var server = new OssConnectionProbeTestServer();
        server.Preflight = (bucket, key, _) =>
        {
            server.Objects[bucket + "/" + key] = ([9, 8, 7], "existing-version");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([9, 8, 7]) });
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_conflict", result.ErrorCode);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.Equal(new byte[] { 9, 8, 7 }, Assert.Single(server.Objects).Value.Payload);
        Assert.Equal("existing-version", Assert.Single(server.Objects).Value.VersionId);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Head);
        Assert.DoesNotContain(server.Requests, request => request.Method != HttpMethod.Head);
        Assert.NotNull(result.Checks);
        Assert.DoesNotContain(result.Checks, check => check.Name == "临时对象清理");
    }

    /// <summary>HEAD 权限或网络拒绝不是写入失败，不尝试 PUT，也不伪造清理检查。</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied")]
    [InlineData(HttpStatusCode.BadGateway, "ServiceUnavailable")]
    public async Task TestConnectionAsync_ReportsPreflightFailureWithoutClaimingWriteDenial(HttpStatusCode status, string code)
    {
        using var server = new OssConnectionProbeTestServer
        {
            Preflight = (_, _, _) => Task.FromResult(OssConnectionProbeTestServer.Error(status, code)),
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_preflight_failed", result.ErrorCode);
        Assert.Contains("写入前检查未通过，未创建测试对象", result.Message);
        Assert.Contains("对象读取权限或网络", result.Message);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.Empty(server.Objects);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Head);
        Assert.DoesNotContain(server.Requests, request => request.Method != HttpMethod.Head);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "身份认证" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "候选容器读取" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象写入" && !check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象回读" && !check.IsSuccessful);
        Assert.DoesNotContain(result.Checks, check => check.Name == "临时对象清理");
        Assert.DoesNotContain("private-untrusted-response", result.ToString());
        Assert.DoesNotContain("resource-private-secret", result.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", result.ToString());
    }

    /// <summary>明确 409 拒绝保护既有数据，即使拒绝同时伴随调用方取消也不能转成未知清理。</summary>
    [Theory]
    [InlineData("FileAlreadyExists", false, "oss_probe_conflict")]
    [InlineData("FileImmutable", false, "oss_probe_write_failed")]
    [InlineData("FileAlreadyExists", true, null)]
    [InlineData("FileImmutable", true, null)]
    public async Task TestConnectionAsync_NeverCleansConfirmedRejectedWrite(string code, bool cancel, string? expectedCode)
    {
        using var server = new OssConnectionProbeTestServer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        server.Upload = (bucket, key, _, _) =>
        {
            server.Objects[bucket + "/" + key] = ([9, 8, 7], "existing-version");
            if (!cancel) return Task.FromResult(OssConnectionProbeTestServer.Error(HttpStatusCode.Conflict, code));
            cancellation.Cancel();
            throw new Oss.ServiceException(409, new Dictionary<string, string>
            {
                ["Code"] = code,
                ["Message"] = "resource-private-secret LTAIResourceTestKey private-untrusted-response",
            });
        };

        Task<InstanceConnectionTestResult> pending = server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        else
        {
            InstanceConnectionTestResult result = await pending;
            Assert.False(result.IsSuccessful);
            Assert.Equal(expectedCode, result.ErrorCode);
            Assert.False(result.RequiresAttentionAfterCancellation);
            Assert.NotNull(result.Checks);
            Assert.DoesNotContain(result.Checks, check => check.Name == "临时对象清理");
            Assert.DoesNotContain("private-untrusted-response", result.ToString());
        }

        Assert.Equal(new byte[] { 9, 8, 7 }, Assert.Single(server.Objects).Value.Payload);
        Assert.Equal("existing-version", Assert.Single(server.Objects).Value.VersionId);
        Assert.DoesNotContain(server.Requests, request => request.Method == HttpMethod.Get || request.Method == HttpMethod.Delete);
    }

    /// <summary>清理权限拒绝或仅生成删除标记都不能让读写测试报告成功。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestConnectionAsync_ReportsCleanupDeniedOrDeleteMarker(bool denied)
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "written-version" };
        server.Delete = (_, _, _, _) =>
        {
            if (denied) return Task.FromResult(OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied"));
            var response = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
            response.Headers.Add("x-oss-delete-marker", "true");
            return Task.FromResult(response);
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_cleanup_failed", result.ErrorCode);
        Assert.True(result.RequiresAttentionAfterCancellation);
        Assert.Single(server.Objects);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "临时对象写入" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象回读" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象清理" && !check.IsSuccessful);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", result.ToString());
    }

    /// <summary>正文正确但实际版本不同也不能通过，清理仍只针对已确认写入的版本。</summary>
    [Fact]
    public async Task TestConnectionAsync_RejectsReadOfDifferentVersion()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "written-version" };
        server.Read = (bucket, key, version, _) =>
        {
            HttpResponseMessage response = server.ReadSuccess(bucket, key, version);
            response.Headers.Remove("x-oss-version-id");
            response.Headers.Add("x-oss-version-id", "different-version");
            return Task.FromResult(response);
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_content_mismatch", result.ErrorCode);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.Empty(server.Objects);
        Assert.Equal("written-version", Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
    }

    /// <summary>身份验证成功后完整列表读取失败，必须保留真实身份检查而将容器读取标失败。</summary>
    [Fact]
    public async Task TestConnectionAsync_RecordsActualChecksWhenFullListingFails()
    {
        using var server = new OssConnectionProbeTestServer
        {
            List = (call, _) => Task.FromResult(call == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("<ListAllMyBucketsResult><Owner><ID>fake-owner</ID></Owner><Buckets /></ListAllMyBucketsResult>"),
                }
                : OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied")),
        };

        InstanceConnectionTestResult result = await server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_list_failed", result.ErrorCode);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "身份认证" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "候选容器读取" && !check.IsSuccessful);
        Assert.DoesNotContain(result.Checks, check => check.Name == "临时对象清理");
        Assert.Empty(server.Requests);
        Assert.DoesNotContain("private-untrusted-response", result.ToString());
    }
}
