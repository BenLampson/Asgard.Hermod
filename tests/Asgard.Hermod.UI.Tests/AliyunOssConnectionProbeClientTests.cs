using System.Net;
using System.Net.Http;
using Oss = AlibabaCloud.OSS.V2;

namespace Asgard.Hermod.UI.Tests;

/// <summary>经真实官方 SDK 与虚构 HTTP 响应验证探针版本回执、清理归属和安全错误。</summary>
public sealed class AliyunOssConnectionProbeClientTests
{
    private const string Bucket = "test-bucket";
    private const string Key = ".hermod-connection-test/unique.probe";

    /// <summary>未版本化、启用和暂停版本化均使用正确版本参数；精确清理保留旧历史版本。</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("probe-version/+one")]
    [InlineData("null")]
    public async Task Probe_RetainsWriteVersionAndReadsAndDeletesOnlyThatVersion(string? versionId)
    {
        byte[] payload = [1, 2, 3];
        var methods = new List<HttpMethod>();
        var versions = new Dictionary<string, byte[]>();
        if (versionId is not null) versions["old-history"] = [9, 8];
        string storedVersion = versionId ?? "latest";
        using var handler = new StorageTestHttpHandler(async (request, token) =>
        {
            methods.Add(request.Method);
            Assert.Equal("/" + Key, request.RequestUri?.AbsolutePath);
            if (request.Method == HttpMethod.Head)
            {
                // 历史版本存在，但当前对象为删除标记；HEAD 不会暴露旧数据供探针误删。
                return Error(HttpStatusCode.NotFound, "NoSuchKey");
            }
            if (request.Method == HttpMethod.Put)
            {
                Assert.Equal("true", Assert.Single(request.Headers.GetValues("x-oss-forbid-overwrite")));
                Assert.NotNull(request.Content);
                Assert.Equal(3, request.Content.Headers.ContentLength);
                Assert.Equal(payload, await request.Content.ReadAsByteArrayAsync(token));
                versions[storedVersion] = payload;
                return Response(HttpStatusCode.OK, versionId: versionId);
            }
            AssertVersionParameter(request, versionId);
            if (request.Method == HttpMethod.Get)
            {
                Assert.Equal("bytes=0-3", request.Headers.Range?.ToString());
                Assert.Equal("standard", Assert.Single(request.Headers.GetValues("x-oss-range-behavior")));
                return Response(HttpStatusCode.PartialContent, versions[storedVersion], versionId);
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.True(versions.Remove(storedVersion));
            return Response(HttpStatusCode.NoContent, versionId: versionId);
        });
        using var client = Create(handler);
        using var body = new MemoryStream(payload, writable: false);

        ObjectStorageProbeWriteResult written = await client.UploadProbeAsync(Bucket, Key, body, body.Length,
            TestContext.Current.CancellationToken);
        Assert.Equal(versionId, written.VersionId);
        ObjectStorageProbeReadResult read = await client.OpenProbeReadAsync(Bucket, Key, written.VersionId, 4,
            TestContext.Current.CancellationToken);
        await using (read.Content)
        {
            Assert.Equal(versionId, read.VersionId);
            Assert.Equal(3, read.Content.Size);
            byte[] actual = new byte[3];
            await read.Content.Body.ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
            Assert.Equal(payload, actual);
        }
        await client.DeleteProbeAsync(Bucket, Key, written.VersionId, TestContext.Current.CancellationToken);

        Assert.Equal([HttpMethod.Head, HttpMethod.Put, HttpMethod.Get, HttpMethod.Delete], methods);
        if (versionId is not null) Assert.Equal(new byte[] { 9, 8 }, Assert.Single(versions).Value);
        else Assert.Empty(versions);
        Assert.True(body.CanRead);
    }

    /// <summary>HEAD 已确认键存在时不发送 PUT，即使版本化容器忽略禁止覆盖请求头也不碰旧对象。</summary>
    [Fact]
    public async Task UploadProbeAsync_RejectsExistingKeyWithoutPuttingOrDeleting()
    {
        var methods = new List<HttpMethod>();
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            methods.Add(request.Method);
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(Response(HttpStatusCode.OK, versionId: "existing-version"));
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1, 2, 3]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
            Bucket, Key, body, body.Length, TestContext.Current.CancellationToken));

        Assert.Equal("FileAlreadyExists", error.ErrorCode);
        Assert.Equal([HttpMethod.Head], methods);
        Assert.Equal(0, body.Position);
        AssertSafe(error);
    }

    /// <summary>PUT 前权限或网络检查失败属于确定未写入，调用方无需猜测或清理该键。</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", "ProbePreflightFailed")]
    [InlineData(HttpStatusCode.BadGateway, "ServiceUnavailable", "ProbePreflightFailed")]
    public async Task UploadProbeAsync_PreflightFailureNeverAttemptsPut(HttpStatusCode status, string code, string expected)
    {
        int requests = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(Error(status, code));
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
            Bucket, Key, body, body.Length, TestContext.Current.CancellationToken));

        Assert.Equal(expected, error.ErrorCode);
        Assert.Contains("未尝试创建测试对象", error.Message);
        Assert.Equal(1, requests);
        AssertSafe(error);
    }

    /// <summary>全部明确 4xx 拒绝均有安全状态，未知 409 不能降级为可盲删的网络失败。</summary>
    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "AccessDenied", "AccessDenied")]
    [InlineData(HttpStatusCode.Forbidden, "SignatureDoesNotMatch", "AuthenticationFailed")]
    [InlineData(HttpStatusCode.Conflict, "FileAlreadyExists", "FileAlreadyExists")]
    [InlineData(HttpStatusCode.Conflict, "FileImmutable", "ProbeWriteRejected")]
    [InlineData(HttpStatusCode.BadRequest, "InvalidArgument", "ProbeWriteRejected")]
    public async Task UploadProbeAsync_MapsDefiniteWriteRejectionsSafely(HttpStatusCode status, string code, string expected)
    {
        var methods = new List<HttpMethod>();
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            methods.Add(request.Method);
            return Task.FromResult(request.Method == HttpMethod.Head ? Error(HttpStatusCode.NotFound, "NoSuchKey") : Error(status, code));
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
            Bucket, Key, body, body.Length, TestContext.Current.CancellationToken));

        Assert.Equal(expected, error.ErrorCode);
        Assert.Equal([HttpMethod.Head, HttpMethod.Put], methods);
        AssertSafe(error);
    }

    /// <summary>服务端已明确拒绝时的并发取消不能把拒绝改成未知上传，防止上层清理既有对象。</summary>
    [Theory]
    [InlineData("FileAlreadyExists", "FileAlreadyExists")]
    [InlineData("FileImmutable", "ProbeWriteRejected")]
    public async Task UploadProbeAsync_PreservesConfirmedConflictWhenCallerAlsoCancels(string code, string expected)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            if (request.Method == HttpMethod.Head) return Task.FromResult(Error(HttpStatusCode.NotFound, "NoSuchKey"));
            Assert.Equal(HttpMethod.Put, request.Method);
            // 传输边界已经获知拒绝，SDK 包装它的同时调用方取消；不需要抢真实网络时序。
            cancellation.Cancel();
            throw new Oss.ServiceException(409, new Dictionary<string, string>
            {
                ["Code"] = code,
                ["Message"] = "resource-private-secret LTAIResourceTestKey",
            });
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
            Bucket, Key, body, body.Length, cancellation.Token));

        Assert.Equal(expected, error.ErrorCode);
        Assert.True(cancellation.IsCancellationRequested);
        AssertSafe(error);
    }

    /// <summary>预取消明确表示未写入；PUT 中取消仍传播取消，适配器不私自删除未知结果。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadProbeAsync_DistinguishesPreflightCancellationFromUnknownWrite(bool cancelBeforeStart)
    {
        var methods = new List<HttpMethod>();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new StorageTestHttpHandler((request, token) =>
        {
            methods.Add(request.Method);
            if (request.Method == HttpMethod.Head) return Task.FromResult(Error(HttpStatusCode.NotFound, "NoSuchKey"));
            Assert.Equal(HttpMethod.Put, request.Method);
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("取消不能继续写入。");
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1]);
        if (cancelBeforeStart) cancellation.Cancel();

        if (cancelBeforeStart)
        {
            ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
                Bucket, Key, body, body.Length, cancellation.Token));
            Assert.Equal("ProbePreflightFailed", error.ErrorCode);
            AssertSafe(error);
            Assert.Empty(methods);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UploadProbeAsync(Bucket, Key, body,
                body.Length, cancellation.Token));
            Assert.Equal([HttpMethod.Head, HttpMethod.Put], methods);
        }
    }

    /// <summary>HEAD 等待期间取消或 404 后取消均没有 PUT，必须保留确定未写入的状态。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UploadProbeAsync_CancellationDuringHeadIsKnownNotToHaveWritten(bool throwDuringHead)
    {
        int requests = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new StorageTestHttpHandler((request, token) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Head, request.Method);
            cancellation.Cancel();
            if (throwDuringHead) token.ThrowIfCancellationRequested();
            return Task.FromResult(Error(HttpStatusCode.NotFound, "NoSuchKey"));
        });
        using var client = Create(handler);
        using var body = new MemoryStream([1]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync(
            Bucket, Key, body, body.Length, cancellation.Token));

        Assert.Equal("ProbePreflightFailed", error.ErrorCode);
        Assert.Equal(1, requests);
        Assert.Equal(0, body.Position);
        AssertSafe(error);
    }

    /// <summary>GET 的实际版本完整返回，调用方可以识别服务器返回错误版本而不只比对正文。</summary>
    [Fact]
    public async Task OpenProbeReadAsync_PreservesActualVersionForCallerValidation()
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            AssertVersionParameter(request, "requested-version");
            return Task.FromResult(Response(HttpStatusCode.PartialContent, [1, 2], "different-version"));
        });
        using var client = Create(handler);

        ObjectStorageProbeReadResult read = await client.OpenProbeReadAsync(Bucket, Key, "requested-version", 3,
            TestContext.Current.CancellationToken);
        await using (read.Content)
        {
            Assert.Equal("different-version", read.VersionId);
            Assert.Equal(2, read.Content.Size);
        }
    }

    /// <summary>远端没有提供长度头时保留未知值，不能从测试流可寻址或已读字节数伪造远端长度。</summary>
    [Fact]
    public async Task OpenProbeReadAsync_PreservesUnknownLengthWhenHeaderIsMissing()
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent([1, 2]),
        }));
        using var client = Create(handler);

        ObjectStorageProbeReadResult read = await client.OpenProbeReadAsync(Bucket, Key, null, 3,
            TestContext.Current.CancellationToken);
        await using (read.Content)
        {
            Assert.Equal(-1, read.Content.Size);
            byte[] actual = new byte[2];
            await read.Content.Body.ReadExactlyAsync(actual, TestContext.Current.CancellationToken);
            Assert.Equal(new byte[] { 1, 2 }, actual);
        }
    }

    /// <summary>删除标记或另一个版本的响应不能伪造永久清理成功，未版本化状态也要拒绝标记。</summary>
    [Theory]
    [InlineData(null, true, "delete-marker")]
    [InlineData("null", true, "null")]
    [InlineData("probe-version", false, "different-version")]
    public async Task DeleteProbeAsync_RejectsMarkerOrMismatchedVersion(string? requestedVersion, bool marker, string returnedVersion)
    {
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            AssertVersionParameter(request, requestedVersion);
            HttpResponseMessage response = Response(HttpStatusCode.NoContent, versionId: returnedVersion);
            if (marker) response.Headers.Add("x-oss-delete-marker", "true");
            return Task.FromResult(response);
        });
        using var client = Create(handler);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.DeleteProbeAsync(
            Bucket, Key, requestedVersion, TestContext.Current.CancellationToken));

        Assert.Equal("ProbeCleanupIncomplete", error.ErrorCode);
        AssertSafe(error);
    }

    /// <summary>探针回读和精确删除权限拒绝均转换成固定消息，不附带 SDK 密钥快照。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadAndDeleteFailures_AreSanitized(bool read)
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(Error(HttpStatusCode.Forbidden, "AccessDenied")));
        using var client = Create(handler);
        Task operation = read
            ? client.OpenProbeReadAsync(Bucket, Key, "probe-version", 4, TestContext.Current.CancellationToken)
            : client.DeleteProbeAsync(Bucket, Key, "probe-version", TestContext.Current.CancellationToken);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => operation);

        Assert.Equal("AccessDenied", error.ErrorCode);
        AssertSafe(error);
    }

    /// <summary>通过生产工厂使用正式 SDK，既有业务客户端仍满足探针可选能力契约。</summary>
    private static IObjectStorageConnectionProbeClient Create(StorageTestHttpHandler handler) =>
        Assert.IsAssignableFrom<IObjectStorageConnectionProbeClient>(AliyunOssResourceTestClient.Create(handler));

    /// <summary>区分缺失参数和字面 null，并验证 SDK 对版本特殊字符的编码。</summary>
    private static void AssertVersionParameter(HttpRequestMessage request, string? versionId) =>
        Assert.Equal(versionId is null ? "" : "?versionId=" + versionId, Uri.UnescapeDataString(request.RequestUri?.Query ?? ""));

    /// <summary>固定假响应保留 HTTP 长度与版本头，让 SDK 执行真实反序列化。</summary>
    private static HttpResponseMessage Response(HttpStatusCode status, byte[]? body = null, string? versionId = null)
    {
        var response = new HttpResponseMessage(status) { Content = new ByteArrayContent(body ?? []) };
        // HTTP 替身不经过真实传输的序列化；有正文的响应显式声明服务端长度头。
        if (body is not null) response.Content.Headers.ContentLength = body.Length;
        if (versionId is not null) response.Headers.Add("x-oss-version-id", versionId);
        return response;
    }

    /// <summary>仅使用虚构诊断字段验证凭据消息不会越过资源适配边界。</summary>
    private static HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent($"<Error><Code>{code}</Code><Message>resource-private-secret LTAIResourceTestKey</Message></Error>"),
    };

    /// <summary>安全业务异常不保留原始服务端正文、签名诊断或内部异常。</summary>
    private static void AssertSafe(ObjectStorageException error)
    {
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", error.ToString());
    }
}
