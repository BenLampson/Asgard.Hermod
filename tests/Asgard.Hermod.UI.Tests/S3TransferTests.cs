using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实 SDK 上传提交、取消回收、限量读取和批量删除的协议边界。</summary>
public sealed class S3TransferTests
{
    /// <summary>禁止覆盖条件在签名前附加到最终提交请求，显式覆盖时不附加。</summary>
    [Theory]
    [InlineData("aws-s3", "", false)]
    [InlineData("minio", "", false)]
    [InlineData("tencent-cos", "x-cos-forbid-overwrite", false)]
    [InlineData("volcengine-tos", "x-tos-forbid-overwrite", false)]
    [InlineData("tencent-cos", "x-cos-forbid-overwrite", true)]
    [InlineData("volcengine-tos", "x-tos-forbid-overwrite", true)]
    public async Task Upload_EnforcesRemoteConditionalWrite(string provider, string nativeHeader, bool overwrite)
    {
        int puts = 0, versionChecks = 0;
        using var handler = new StorageTestHttpHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get)
            {
                versionChecks++;
                Assert.Contains("versioning", request.RequestUri!.Query);
                return S3TestClient.Xml("<VersioningConfiguration/>");
            }
            puts++;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(!overwrite, request.Headers.Contains("If-None-Match"));
            if (nativeHeader.Length > 0)
            {
                Assert.Equal(!overwrite, request.Headers.Contains(nativeHeader));
                if (!overwrite)
                {
                    Assert.Equal("true", request.Headers.GetValues(nativeHeader).Single());
                    Assert.Contains(nativeHeader, request.Headers.GetValues("Authorization").Single());
                }
            }
            Assert.Equal("payload", await request.Content!.ReadAsStringAsync(token));
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.TryAddWithoutValidation("ETag", "\"etag\"");
            return response;
        });
        using var client = S3TestClient.Provider(handler, provider).CreateClient(S3TestClient.Options(provider), null);
        using var body = new MemoryStream("payload"u8.ToArray());
        await client.UploadAsync("test-bucket", "a.txt", body, body.Length, overwrite, null, S3TestClient.Token);
        Assert.True(body.CanRead);
        Assert.Equal(1, puts);
        Assert.Equal(nativeHeader.Length > 0 && !overwrite ? 1 : 0, versionChecks);
    }

    /// <summary>厂商原生覆盖保护不支持版本化桶时，在发送正文前明确失败。</summary>
    [Theory]
    [InlineData("tencent-cos", "Enabled")]
    [InlineData("volcengine-tos", "Suspended")]
    public async Task ConditionalUpload_RejectsUnsupportedVersionedBucket(string provider, string status)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(S3TestClient.Xml($"<VersioningConfiguration><Status>{status}</Status></VersioningConfiguration>"));
        });
        using var client = S3TestClient.Provider(handler, provider).CreateClient(S3TestClient.Options(provider), null);
        using var body = new MemoryStream("payload"u8.ToArray());
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadAsync("test-bucket", "key", body, body.Length, false, null, S3TestClient.Token));
        Assert.Equal("ConditionalWriteUnsupported", error.ErrorCode);
        Assert.Equal(1, calls);
    }

    /// <summary>大文件按多个分片发送，禁止覆盖条件由最终 Complete 请求执行。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multipart_CompletesConditionallyOrAbortsAfterCancellation(bool cancel)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(S3TestClient.Token);
        int parts = 0, completed = 0, aborted = 0;
        long received = 0;
        using var handler = new StorageTestHttpHandler(async (request, token) =>
        {
            string query = request.RequestUri!.Query;
            if (request.Method == HttpMethod.Delete)
            {
                aborted++;
                Assert.False(token.IsCancellationRequested);
                Assert.Contains("uploadId=upload-id", query);
                return new(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Post && query.Contains("uploads", StringComparison.Ordinal))
                return S3TestClient.Xml("<InitiateMultipartUploadResult><UploadId>upload-id</UploadId></InitiateMultipartUploadResult>");
            if (request.Method == HttpMethod.Put)
            {
                parts++;
                received += (await request.Content!.ReadAsByteArrayAsync(token)).Length;
                if (cancel)
                {
                    cancellation.Cancel();
                    throw new OperationCanceledException(cancellation.Token);
                }
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation("ETag", $"\"part-{parts}\"");
                return response;
            }
            completed++;
            Assert.Equal("*", request.Headers.GetValues("If-None-Match").Single());
            Assert.Contains("part-2", await request.Content!.ReadAsStringAsync(token));
            return S3TestClient.Xml("<CompleteMultipartUploadResult><ETag>\"complete\"</ETag></CompleteMultipartUploadResult>");
        });
        using var client = S3TestClient.Provider(handler, "minio").CreateClient(S3TestClient.Options("minio"), null);
        using var body = new MemoryStream(new byte[9 * 1024 * 1024]);
        Task upload = client.UploadAsync("test-bucket", "large.bin", body, body.Length, false, null, cancellation.Token);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload);
        else await upload;
        Assert.True(body.CanRead);
        Assert.Equal(cancel ? 1 : 2, parts);
        Assert.Equal(cancel ? 0 : 1, completed);
        Assert.Equal(cancel ? 1 : 0, aborted);
        if (!cancel) Assert.Equal(body.Length, received);
    }

    /// <summary>HTTP 200 内部的部分删除失败也必须提示失败，不能误报全部成功。</summary>
    [Fact]
    public async Task Delete_PartialFailureIsNotSuccess()
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(S3TestClient.Xml(
            "<DeleteResult><Deleted><Key>a</Key></Deleted><Error><Key>b</Key><Code>AccessDenied</Code><Message>private details</Message></Error></DeleteResult>")));
        using var client = S3TestClient.Provider(handler, "aws-s3").CreateClient(S3TestClient.Options(), null);
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.DeleteAsync("test-bucket", ["a", "b"], S3TestClient.Token));
        Assert.Equal("PartialDeleteFailure", error.ErrorCode);
        Assert.Null(error.InnerException);
    }

    /// <summary>预览发送 Range，保留服务端返回长度并释放响应正文。</summary>
    [Fact]
    public async Task Preview_UsesRangeAndOwnsResponseStream()
    {
        var responseBody = new MemoryStream("abc"u8.ToArray());
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Assert.Equal("bytes=0-2", request.Headers.Range!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(responseBody) });
        });
        using var client = S3TestClient.Provider(handler, "aws-s3").CreateClient(S3TestClient.Options(), null);
        var result = await client.OpenReadAsync("test-bucket", "a.txt", 3, S3TestClient.Token);
        await using (result)
        {
            using var reader = new StreamReader(result.Body);
            Assert.Equal("abc", await reader.ReadToEndAsync(S3TestClient.Token));
        }
        Assert.False(responseBody.CanRead);
    }
}
