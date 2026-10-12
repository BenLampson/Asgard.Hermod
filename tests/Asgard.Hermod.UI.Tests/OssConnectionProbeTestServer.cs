using System.Net;
using System.Net.Http;
using System.Xml.Linq;
using Asgard.Hermod.Infrastructure.Storage;

namespace Asgard.Hermod.UI.Tests;

/// <summary>在已有 HTTP 边界模拟真实 SDK 的探测交互，仅使用虚构凭据和内存中的临时对象。</summary>
public sealed class OssConnectionProbeTestServer : IDisposable
{
    /// <summary>全部候选容器，不访问账户或真实云资源。</summary>
    public IReadOnlyList<ObjectStorageBucket> Buckets { get; set; } = [new("test-bucket", "cn-hangzhou")];
    /// <summary>分别模拟身份请求与后续完整容器列表请求的结果。</summary>
    public Func<int, CancellationToken, Task<HttpResponseMessage>>? List { get; set; }
    /// <summary>列表请求次数独立于对象请求，便于区分成功身份验证和失败容器枚举。</summary>
    private int _listCalls;
    /// <summary>版本化响应的模拟版本，字面 null 用于暂停版本化容器。</summary>
    public string? VersionId { get; set; }
    /// <summary>模拟可配置的写入拒绝、响应丢失和网络取消。</summary>
    public Func<string, string, byte[], CancellationToken, Task<HttpResponseMessage>>? Upload { get; set; }
    /// <summary>模拟测试随机键已经存在，或其预检读取被拒绝。</summary>
    public Func<string, string, CancellationToken, Task<HttpResponseMessage>>? Preflight { get; set; }
    /// <summary>模拟回读拒绝、错误内容或等待用户取消。</summary>
    public Func<string, string, string?, CancellationToken, Task<HttpResponseMessage>>? Read { get; set; }
    /// <summary>模拟精确版本清理失败，并记录清理使用的独立取消令牌。</summary>
    public Func<string, string, string?, CancellationToken, Task<HttpResponseMessage>>? Delete { get; set; }
    /// <summary>当前模拟对象及其版本；每个测试实例独立。</summary>
    public Dictionary<string, (byte[] Payload, string? VersionId)> Objects { get; } = new(StringComparer.Ordinal);
    /// <summary>仅记录方法、容器、对象和版本，不记录签名或身份头。</summary>
    public List<(HttpMethod Method, string Bucket, string Key, string? VersionId)> Requests { get; } = [];
    /// <summary>共用已存在的请求边界，不创建实际网络连接。</summary>
    public StorageTestHttpHandler Handler { get; }

    /// <summary>构造完全隔离的 SDK HTTP 请求处理器。</summary>
    public OssConnectionProbeTestServer() => Handler = new StorageTestHttpHandler(HandleAsync);

    /// <summary>创建生产连接服务商，向其提供与资源 SDK 相同的替身 HTTP 工厂。</summary>
    public AliyunOssStorageProvider CreateProvider(TimeSpan? timeout = null) =>
        new(new StorageTestHttpClientFactory(Handler), timeout: timeout);

    /// <summary>通用探测器可注入较短清理期限，验证独立清理取消行为。</summary>
    public ObjectStorageConnectionProbe CreateProbe(TimeSpan? cleanupTimeout = null) =>
        new(new AliyunOssObjectStorageProvider(new StorageTestHttpClientFactory(Handler)), cleanupTimeout);

    /// <summary>只处理官方 SDK 需要的列表、预检、临时对象 PUT/GET/DELETE。</summary>
    private async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken token)
    {
        Uri uri = request.RequestUri!;
        Assert.Equal("https", uri.Scheme);
        if (uri.Host == "oss-cn-hangzhou.aliyuncs.com")
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            int listCall = Interlocked.Increment(ref _listCalls);
            if (List is not null) return await List(listCall, token);
            var root = new XElement("ListAllMyBucketsResult", new XElement("Owner", new XElement("ID", "fake-owner")),
                new XElement("IsTruncated", "false"), new XElement("Buckets", Buckets.Select(bucket =>
                    new XElement("Bucket", new XElement("Name", bucket.Name), new XElement("Region", bucket.Region)))));
            return new(HttpStatusCode.OK) { Content = new StringContent(root.ToString(SaveOptions.DisableFormatting)) };
        }
        string bucketName = uri.Host.Split('.')[0];
        string key = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
        string? version = Query(uri, "versionId");
        Requests.Add((request.Method, bucketName, key, version));
        Assert.StartsWith(ObjectStorageConnectionProbe.ObjectPrefix, key);
        string address = bucketName + "/" + key;
        if (request.Method == HttpMethod.Head)
        {
            if (Preflight is not null) return await Preflight(bucketName, key, token);
            if (!Objects.TryGetValue(address, out var existing)) return Error(HttpStatusCode.NotFound, "NoSuchKey");
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
            response.Content.Headers.ContentLength = existing.Payload.Length;
            if (existing.VersionId is not null) response.Headers.Add("x-oss-version-id", existing.VersionId);
            return response;
        }
        if (request.Method == HttpMethod.Put)
        {
            Assert.Equal("true", Assert.Single(request.Headers.GetValues("x-oss-forbid-overwrite")));
            byte[] payload = await request.Content!.ReadAsByteArrayAsync(token);
            return Upload is not null ? await Upload(bucketName, key, payload, token) : PutSuccess(bucketName, key, payload);
        }
        if (request.Method == HttpMethod.Get)
        {
            Assert.NotNull(request.Headers.Range);
            return Read is not null ? await Read(bucketName, key, version, token) : ReadSuccess(bucketName, key, version);
        }
        Assert.Equal(HttpMethod.Delete, request.Method);
        return Delete is not null ? await Delete(bucketName, key, version, token) : DeleteSuccess(bucketName, key, version);
    }

    /// <summary>模拟确认写入成功，同时保留版本回执。</summary>
    public HttpResponseMessage PutSuccess(string bucket, string key, byte[] payload)
    {
        Objects[bucket + "/" + key] = (payload, VersionId);
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([]) };
        response.Headers.ETag = new("\"test-etag\"");
        if (VersionId is not null) response.Headers.Add("x-oss-version-id", VersionId);
        return response;
    }

    /// <summary>返回正文和实际版本，指定版本必须和模拟对象相同。</summary>
    public HttpResponseMessage ReadSuccess(string bucket, string key, string? version)
    {
        if (!Objects.TryGetValue(bucket + "/" + key, out var existing)) return Error(HttpStatusCode.NotFound, "NoSuchKey");
        if (version is not null && version != existing.VersionId) return Error(HttpStatusCode.NotFound, "NoSuchVersion");
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(existing.Payload) };
        response.Content.Headers.ContentLength = existing.Payload.Length;
        response.Content.Headers.ContentRange = new(0, existing.Payload.Length - 1, existing.Payload.Length);
        if (existing.VersionId is not null) response.Headers.Add("x-oss-version-id", existing.VersionId);
        return response;
    }

    /// <summary>缺少精确版本的删除只生成标记，正确版本删除才移除模拟对象。</summary>
    public HttpResponseMessage DeleteSuccess(string bucket, string key, string? version)
    {
        string address = bucket + "/" + key;
        var response = new HttpResponseMessage(HttpStatusCode.NoContent) { Content = new ByteArrayContent([]) };
        if (Objects.TryGetValue(address, out var existing) && existing.VersionId is not null && version is null)
        {
            response.Headers.Add("x-oss-delete-marker", "true");
            return response;
        }
        if (existing.VersionId is not null) Assert.Equal(existing.VersionId, version);
        Objects.Remove(address);
        return response;
    }

    /// <summary>错误正文故意含虚构私密内容，生产结果必须只使用固定白名单文案。</summary>
    public static HttpResponseMessage Error(HttpStatusCode status, string code) => new(status)
    {
        Content = new StringContent(new XElement("Error", new XElement("Code", code),
            new XElement("Message", "resource-private-secret LTAIResourceTestKey private-untrusted-response")).ToString()),
    };

    /// <summary>仅从已观察的请求解析版本参数，不构造签名或复制 SDK 的版本实现。</summary>
    private static string? Query(Uri uri, string name) => uri.Query.TrimStart('?').Split('&')
        .Select(value => value.Split('=', 2)).Where(value => value.Length == 2 && value[0] == name)
        .Select(value => Uri.UnescapeDataString(value[1])).FirstOrDefault();

    /// <inheritdoc />
    public void Dispose() => Handler.Dispose();
}
