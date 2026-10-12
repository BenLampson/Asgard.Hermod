using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>经 SDK 边界验证取消和超时不会跳过清理，也不会隐藏必须处理的残留对象。</summary>
public sealed class AliyunOssConnectionProbeCancellationTests
{
    /// <summary>用户取消回读后，已确认的对象版本仍以独立令牌清理，再传播正常取消。</summary>
    [Fact]
    public async Task TestConnectionAsync_CleansKnownVersionAfterCallerCancelsRead()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "written-version" };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Read = async (_, _, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        };
        server.Delete = (bucket, key, version, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            Assert.NotEqual(cancellation.Token, token);
            return Task.FromResult(server.DeleteSuccess(bucket, key, version));
        };

        Task<InstanceConnectionTestResult> pending = server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(server.Objects);
        Assert.Equal("written-version", Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
    }

    /// <summary>用户取消时清理被拒绝，仍返回需要处理的安全失败与临时对象位置。</summary>
    [Fact]
    public async Task TestConnectionAsync_ReturnsCleanupFailureDespiteCallerCancellation()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "written-version" };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Read = async (_, _, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        };
        server.Delete = (_, _, _, token) =>
        {
            Assert.False(token.IsCancellationRequested);
            return Task.FromResult(OssConnectionProbeTestServer.Error(HttpStatusCode.Forbidden, "AccessDenied"));
        };

        Task<InstanceConnectionTestResult> pending = server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        InstanceConnectionTestResult result = await pending;

        Assert.True(cancellation.IsCancellationRequested);
        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_cleanup_failed", result.ErrorCode);
        Assert.True(result.RequiresAttentionAfterCancellation);
        string address = Assert.Single(server.Objects).Key;
        Assert.Contains("容器：test-bucket", result.Message);
        Assert.Contains("对象 Key：" + address["test-bucket/".Length..], result.Message);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "临时对象清理" && !check.IsSuccessful);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", result.ToString());
    }

    /// <summary>PUT 已落库但响应等待被取消，先回读确认唯一正文和版本，再精确清理。</summary>
    [Fact]
    public async Task TestConnectionAsync_ResolvesUnknownUploadAfterCallerCancellation()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "ambiguous-version" };
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Upload = async (bucket, key, payload, token) =>
        {
            using HttpResponseMessage ignored = server.PutSuccess(bucket, key, payload);
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        };

        Task<InstanceConnectionTestResult> pending = server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(server.Objects);
        Assert.Null(Assert.Single(server.Requests, request => request.Method == HttpMethod.Get).VersionId);
        Assert.Equal("ambiguous-version", Assert.Single(server.Requests, request => request.Method == HttpMethod.Delete).VersionId);
    }

    /// <summary>整体超时保留已通过的身份、列表和写入检查，而清理仍有独立期限。</summary>
    [Fact]
    public async Task TestConnectionAsync_ReportsDeadlineWithActualChecksAfterCleanup()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "timed-version" };
        server.Read = async (_, _, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.OK);
        };

        InstanceConnectionTestResult result = await server.CreateProvider(TimeSpan.FromSeconds(1)).TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_timeout", result.ErrorCode);
        Assert.False(result.RequiresAttentionAfterCancellation);
        Assert.Empty(server.Objects);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "身份认证" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "候选容器读取" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象写入" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象回读" && !check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象清理" && check.IsSuccessful);
    }

    /// <summary>独立清理期限到期也不能伪造读写成功，必须返回残留位置。</summary>
    [Fact]
    public async Task GenericProbe_ReportsCleanupTimeoutAfterSuccessfulReadWrite()
    {
        using var server = new OssConnectionProbeTestServer { VersionId = "uncleaned-version" };
        server.Delete = async (_, _, _, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.NoContent);
        };

        InstanceConnectionTestResult result = await server.CreateProbe(TimeSpan.FromMilliseconds(30)).TestAsync(
            AliyunOssResourceTestClient.Options(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("storage_probe_cleanup_failed", result.ErrorCode);
        Assert.True(result.RequiresAttentionAfterCancellation);
        Assert.Single(server.Objects);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => check.Name == "临时对象写入" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象回读" && check.IsSuccessful);
        Assert.Contains(result.Checks, check => check.Name == "临时对象清理" && !check.IsSuccessful);
        Assert.DoesNotContain("resource-private-secret", result.ToString());
    }

    /// <summary>预检期间取消确定未尝试 PUT，不回读或删除，也不提示未知上传残留。</summary>
    [Fact]
    public async Task TestConnectionAsync_CancelsPreflightWithoutCleanupOrAttention()
    {
        using var server = new OssConnectionProbeTestServer();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        server.Preflight = async (_, _, token) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new(HttpStatusCode.NotFound);
        };

        Task<InstanceConnectionTestResult> pending = server.CreateProvider().TestConnectionAsync(
            AliyunOssResourceTestClient.Options(), cancellation.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Empty(server.Objects);
        Assert.Single(server.Requests, request => request.Method == HttpMethod.Head);
        Assert.DoesNotContain(server.Requests, request => request.Method != HttpMethod.Head);
    }
}
