using System.Net;
using System.Net.Http;
using System.Xml.Linq;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实 SDK 的覆盖条件、分片清理、严格批量删除与取消边界。</summary>
public sealed class AliyunOssObjectStorageWriteTests
{
    /// <summary>小对象直接 PUT，未授权覆盖时写入请求本身带服务端禁止覆盖条件。</summary>
    [Theory]
    [InlineData(false, "true")]
    [InlineData(true, "false")]
    public async Task UploadAsync_SendsServerSideOverwriteCondition(bool overwrite, string expectedHeader)
    {
        int requests = 0;
        using var handler = new StorageTestHttpHandler(async (request, cancellationToken) =>
        {
            requests++;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(expectedHeader, Assert.Single(request.Headers.GetValues("x-oss-forbid-overwrite")));
            Assert.NotNull(request.Content);
            Assert.Equal(new byte[] { 1, 2, 3 }, await request.Content.ReadAsByteArrayAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") };
        });
        using var client = AliyunOssResourceTestClient.Create(handler);
        using var body = new MemoryStream([1, 2, 3]);
        var progress = new RecordingObjectStorageProgress { ThrowOnReport = true };

        await client.UploadAsync("test-bucket", "small.bin", body, 3, overwrite, progress, TestContext.Current.CancellationToken);

        Assert.Equal(1, requests);
        Assert.Equal(3, progress.Values.Last().TransferredBytes);
    }

    /// <summary>确认期间出现重名对象时，完成分片请求仍拒绝覆盖并中止未完成上传。</summary>
    [Fact]
    public async Task UploadAsync_ForbidsOverwriteAtMultipartCompletionAndAbortsOnConflict()
    {
        int parts = 0;
        int completions = 0;
        int aborts = 0;
        using var handler = new StorageTestHttpHandler(async (request, cancellationToken) =>
        {
            string query = request.RequestUri?.Query ?? "";
            if (request.Method == HttpMethod.Post && query.Contains("uploads", StringComparison.Ordinal))
            {
                Assert.Equal("true", Assert.Single(request.Headers.GetValues("x-oss-forbid-overwrite")));
                return Response(HttpStatusCode.OK, "<InitiateMultipartUploadResult><Bucket>test-bucket</Bucket><Key>large.bin</Key>" +
                    "<UploadId>upload-id</UploadId></InitiateMultipartUploadResult>");
            }
            if (request.Method == HttpMethod.Put)
            {
                parts++;
                Assert.NotNull(request.Content);
                byte[] bytes = await request.Content.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(parts == 1 ? 8 * 1024 * 1024 : 1, bytes.Length);
                var result = Response(HttpStatusCode.OK, "");
                result.Headers.ETag = new($"\"part-{parts}\"");
                return result;
            }
            if (request.Method == HttpMethod.Post)
            {
                completions++;
                Assert.Equal("true", Assert.Single(request.Headers.GetValues("x-oss-forbid-overwrite")));
                return Response(HttpStatusCode.Conflict, "<Error><Code>FileAlreadyExists</Code>" +
                    "<Message>resource-private-secret LTAIResourceTestKey</Message></Error>");
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Contains("uploadId=upload-id", query);
            aborts++;
            return Response(HttpStatusCode.NoContent, "");
        });
        using var client = AliyunOssResourceTestClient.Create(handler);
        using var body = new MemoryStream(new byte[8 * 1024 * 1024 + 1]);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadAsync("test-bucket", "large.bin", body,
            body.Length, overwrite: false, progress: null, TestContext.Current.CancellationToken));

        Assert.Equal("FileAlreadyExists", error.ErrorCode);
        Assert.Equal(2, parts);
        Assert.Equal(1, completions);
        Assert.Equal(1, aborts);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", error.ToString());
        Assert.True(body.CanRead);
    }

    /// <summary>分片中取消继续传播 OCE，清理请求使用独立取消期限而非已取消令牌。</summary>
    [Fact]
    public async Task UploadAsync_AbortsMultipartWithIndependentCleanupTokenOnCancellation()
    {
        int aborts = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var handler = new StorageTestHttpHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Post)
                return Response(HttpStatusCode.OK, "<InitiateMultipartUploadResult><UploadId>upload-id</UploadId></InitiateMultipartUploadResult>");
            if (request.Method == HttpMethod.Put)
            {
                Assert.NotNull(request.Content);
                await request.Content.CopyToAsync(Stream.Null, token);
                cancellation.Cancel();
                var result = Response(HttpStatusCode.OK, "");
                result.Headers.ETag = new("\"part-1\"");
                return result;
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.False(token.IsCancellationRequested);
            aborts++;
            return Response(HttpStatusCode.NoContent, "");
        });
        using var client = AliyunOssResourceTestClient.Create(handler);
        using var body = new MemoryStream(new byte[8 * 1024 * 1024 + 1]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.UploadAsync("test-bucket", "large.bin", body,
            body.Length, overwrite: false, progress: null, cancellation.Token));

        Assert.Equal(1, aborts);
    }

    /// <summary>批量删除要求服务商返回每个确认键的成功状态，缺失键不能伪造完全成功。</summary>
    [Fact]
    public async Task DeleteAsync_SendsExactKeysAndReportsPartialServerFailure()
    {
        using var handler = new StorageTestHttpHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Contains("delete", request.RequestUri?.Query);
            Assert.NotNull(request.Content);
            XDocument document = XDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("false", document.Root?.Element("Quiet")?.Value);
            Assert.Equal(["selected-a.txt", "docs/selected-b.txt"], document.Root?.Elements("Object").Select(item => item.Element("Key")?.Value));
            return Response(HttpStatusCode.OK, "<DeleteResult><Deleted><Key>selected-a.txt</Key></Deleted>" +
                "<Error><Key>docs/selected-b.txt</Key><Code>AccessDenied</Code><Message>resource-private-secret</Message></Error></DeleteResult>");
        });
        using var client = AliyunOssResourceTestClient.Create(handler);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.DeleteAsync("test-bucket",
            ["selected-a.txt", "docs/selected-b.txt"], TestContext.Current.CancellationToken));

        Assert.Equal("PartialDelete", error.ErrorCode);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
        Assert.Null(error.InnerException);
    }

    /// <summary>网络异常也可携带签名和凭据，资源适配器必须转换成固定安全消息。</summary>
    [Fact]
    public async Task SdkFailures_AreSanitizedWithoutInnerException()
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new HttpRequestException("resource-private-secret LTAIResourceTestKey"));
        using var client = AliyunOssResourceTestClient.Create(handler);

        ObjectStorageException error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.ObjectExistsAsync("test-bucket",
            "file.txt", TestContext.Current.CancellationToken));

        Assert.Equal("StorageOperationFailed", error.ErrorCode);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("resource-private-secret", error.ToString());
        Assert.DoesNotContain("LTAIResourceTestKey", error.ToString());
    }

    /// <summary>虚构 XML 响应足以验证官方 SDK 的解析和异常封装。</summary>
    private static HttpResponseMessage Response(HttpStatusCode code, string xml) => new(code) { Content = new StringContent(xml) };
}
