using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>通过完整连接探测验证写入回执、指定版本回读和永久清理。</summary>
public sealed class S3ProbeTests
{
    /// <summary>字符串 null 也是有效版本；删除标记不能被当成永久清理成功。</summary>
    [Theory]
    [InlineData("aws-s3", "version-1", false)]
    [InlineData("minio", "null", false)]
    [InlineData("tencent-cos", "version-1", false)]
    [InlineData("volcengine-tos", "version-1", true)]
    public async Task ConnectionTest_UsesExactVersionAndDetectsIncompleteCleanup(string provider, string version, bool marker)
    {
        byte[]? payload = null;
        string? objectPath = null;
        int writes = 0, reads = 0, deletes = 0;
        using var handler = new StorageTestHttpHandler(async (request, token) =>
        {
            if (request.RequestUri!.Query.Contains("list-type=2", StringComparison.Ordinal))
                return S3TestClient.Xml("<ListBucketResult/>");
            if (request.Method == HttpMethod.Head) return new(HttpStatusCode.NotFound);
            if (request.Method == HttpMethod.Put)
            {
                writes++;
                objectPath = request.RequestUri.AbsolutePath;
                Assert.Contains(ObjectStorageConnectionProbe.ObjectPrefix, objectPath);
                payload = await request.Content!.ReadAsByteArrayAsync(token);
                var response = new HttpResponseMessage(HttpStatusCode.OK);
                response.Headers.TryAddWithoutValidation("x-amz-version-id", version);
                return response;
            }
            Assert.Equal(objectPath, request.RequestUri.AbsolutePath);
            Assert.Contains("versionId=" + version, request.RequestUri.Query);
            if (request.Method == HttpMethod.Get)
            {
                reads++;
                Assert.NotNull(request.Headers.Range);
                var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(payload!) };
                response.Headers.TryAddWithoutValidation("x-amz-version-id", version);
                return response;
            }
            deletes++;
            Assert.Equal(HttpMethod.Delete, request.Method);
            var deleted = new HttpResponseMessage(HttpStatusCode.NoContent);
            deleted.Headers.TryAddWithoutValidation("x-amz-version-id", version);
            if (marker) deleted.Headers.TryAddWithoutValidation("x-amz-delete-marker", "true");
            return deleted;
        });
        var result = await S3TestClient.Provider(handler, provider).TestConnectionAsync(
            S3TestClient.Options(provider, ("bucket", "test-bucket")), S3TestClient.Token);
        Assert.Equal(!marker, result.IsSuccessful);
        Assert.Equal(marker, result.RequiresAttentionAfterCancellation);
        Assert.Equal(1, writes);
        Assert.Equal(1, reads);
        Assert.Equal(1, deletes);
        if (marker) Assert.Equal("storage_probe_cleanup_failed", result.ErrorCode);
    }

    /// <summary>写入前无法确认对象不存在时，不创建或删除任何对象。</summary>
    [Fact]
    public async Task ProbePreflight_DenialDoesNotWriteOrDelete()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(HttpMethod.Head, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        });
        using var storage = S3TestClient.Provider(handler, "minio").CreateClient(S3TestClient.Options("minio"), null);
        var client = Assert.IsAssignableFrom<IObjectStorageConnectionProbeClient>(storage);
        using var body = new MemoryStream("probe"u8.ToArray());
        var error = await Assert.ThrowsAsync<ObjectStorageException>(() => client.UploadProbeAsync("test-bucket", "random.probe", body, body.Length, S3TestClient.Token));
        Assert.Equal("ProbePreflightFailed", error.ErrorCode);
        Assert.Equal(1, calls);
    }
}
