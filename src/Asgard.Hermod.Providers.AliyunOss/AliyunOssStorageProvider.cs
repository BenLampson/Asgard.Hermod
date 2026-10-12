using System.Net;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>先验证官方 V4 身份，再使用通用对象探测器验证临时对象的写入、回读和清理。</summary>
public sealed class AliyunOssStorageProvider : IStorageProvider
{
    /// <summary>供 DI 配置的专属客户端名称；连接参数永不进入请求 URL。</summary>
    public const string HttpClientName = "Hermod.AliyunOss";
    /// <summary>实例配置和向导复用的稳定连接器标识。</summary>
    public const string ProviderId = "aliyun-oss";
    /// <summary>存储是实例类型，OSS 是该类型下的服务商实现。</summary>
    public const string TypeId = "storage";
    /// <summary>ListBuckets 查询账户桶列表，不要求用户预先选择具体 Bucket。</summary>
    private const string Region = "cn-hangzhou";
    /// <summary>固定官方 HTTPS 域，禁止凭据跟随任意 Endpoint 或重定向外发。</summary>
    private static readonly Uri ListBucketsUri = new("https://oss-cn-hangzhou.aliyuncs.com/?max-keys=1");
    /// <summary>客户端生命周期和弹性由应用 DI 统一控制。</summary>
    private readonly IHttpClientFactory _clients;
    /// <summary>签名时钟支持确定性验证，生产使用 UTC 系统时间。</summary>
    private readonly TimeProvider _time;
    /// <summary>包含重试和读取响应的整体连接测试期限。</summary>
    private readonly TimeSpan _timeout;

    /// <summary>显式保留可测试的时钟和期限；生产注册使用默认二十秒期限。</summary>
    public AliyunOssStorageProvider(IHttpClientFactory clients, TimeProvider? time = null, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(clients);
        _clients = clients;
        _time = time ?? TimeProvider.System;
        _timeout = timeout ?? TimeSpan.FromSeconds(20);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    /// <inheritdoc />
    public InstanceProviderDescriptor Descriptor { get; } = new(ProviderId, Text.Get("AlibabaCloudOSS"), "Object Storage Service", "cloud",
        new(TypeId, Text.Get("Storage"), Text.Get("ConnectToObjectStorage"), "database"),
        Array.AsReadOnly<InstanceConfigurationField>([
            new("accessKeyId", "AccessKey ID", InstanceFieldKind.Text, MaxLength: 128,
                IsSensitive: true, ValidationRule: InstanceFieldValidationRule.AsciiLettersAndDigits),
            new("accessKeySecret", "AccessKey Secret", InstanceFieldKind.Secret, MaxLength: 256, IsSensitive: true),
        ]))
    {
        ConnectionTestDescription = Text.Get("TheTestListsBucketsCreatesARandomTemporaryObjectUnderHermodConnectionTestInAnAvailabl"),
    };

    /// <inheritdoc />
    public async Task<InstanceConnectionTestResult> TestConnectionAsync(InstanceConnectionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(options.TypeId, TypeId, StringComparison.Ordinal) ||
            !string.Equals(options.ProviderId, ProviderId, StringComparison.Ordinal))
            return Failure("instance_provider_mismatch", Text.Get("TheConnectionConfigurationDoesNotMatchTheSelectedInstanceTypeOrProvider"));
        if (options.Parameters is null || !options.Parameters.TryGetValue("accessKeyId", out string? accessKeyId) ||
            !options.Parameters.TryGetValue("accessKeySecret", out string? accessKeySecret) ||
            string.IsNullOrWhiteSpace(accessKeyId) || accessKeyId.Length > 128 ||
            !accessKeyId.All(char.IsAsciiLetterOrDigit) || string.IsNullOrWhiteSpace(accessKeySecret) ||
            accessKeySecret.Length > 256 || accessKeySecret.Any(char.IsControl))
            return Failure("oss_invalid_credentials", Text.Get("EnterAValidAccessKeyIDAndAccessKeySecret"));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_timeout);
        try
        {
            using HttpClient client = _clients.CreateClient(HttpClientName);
            // ResponseContentRead 将网络响应完整读取纳入取消期限，并限制异常响应的体积。
            client.MaxResponseContentBufferSize = 1024 * 1024;
            using HttpRequestMessage request = CreateRequest(accessKeyId, accessKeySecret);
            using HttpResponseMessage response = await client.SendAsync(request,
                HttpCompletionOption.ResponseContentRead, deadline.Token).ConfigureAwait(false);
            XDocument? document = await ReadResponseAsync(response, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return MapFailure(response.StatusCode, document?.Root?.Element("Code")?.Value);
            if (response.StatusCode != HttpStatusCode.OK || document?.Root?.Name.LocalName != "ListAllMyBucketsResult" ||
                document.Root.Element("Owner") is null || document.Root.Element("Buckets") is null)
                return Failure("oss_invalid_response", Text.Get("OSSReturnedAnUnverifiableResponseTryAgainLater"));
            // 固定此次已验证的凭据，避免测试期间外部字典变更导致不同请求使用不同身份。
            var snapshot = new InstanceConnectionOptions
            {
                TypeId = TypeId,
                ProviderId = ProviderId,
                Parameters = new Dictionary<string, string>
                {
                    ["accessKeyId"] = accessKeyId,
                    ["accessKeySecret"] = accessKeySecret,
                },
            };
            var probe = new ObjectStorageConnectionProbe(new AliyunOssObjectStorageProvider(_clients));
            InstanceConnectionTestResult result = await probe.TestAsync(snapshot, deadline.Token).ConfigureAwait(false);
            result = result with
            {
                ErrorCode = result.ErrorCode?.Replace("storage_probe_", "oss_probe_", StringComparison.Ordinal),
                Checks = Array.AsReadOnly<InstanceConnectionCheck>([new(Text.Get("Authentication"), true), .. (result.Checks ?? [])]),
            };
            // 清理失败须在用户取消后仍返回安全位置；普通取消继续遵循原有取消契约。
            if (result.RequiresAttentionAfterCancellation) return result;
            cancellationToken.ThrowIfCancellationRequested();
            if (deadline.IsCancellationRequested || result.ErrorCode == "oss_probe_cancelled")
                return result with { IsSuccessful = false, ErrorCode = "oss_timeout", Message = Text.Get("ConnectionTestTimedOutKnownTemporaryObjectsWereCleanedUpCheckTheNetworkAndTryAgain") };
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return Failure("oss_timeout", Text.Get("ConnectionTestTimedOutCheckTheNetworkAndTryAgain"));
        }
        catch (TimeoutRejectedException)
        {
            return Failure("oss_timeout", Text.Get("ConnectionTestTimedOutCheckTheNetworkAndTryAgain"));
        }
        catch (BrokenCircuitException)
        {
            return Failure("oss_unavailable", Text.Get("OSSIsTemporarilyUnavailableTryAgainLater"));
        }
        catch (XmlException)
        {
            return Failure("oss_invalid_response", Text.Get("OSSReturnedAnUnverifiableResponseTryAgainLater"));
        }
        catch (Exception)
        {
            // 第三方、TLS 或网络异常可能包含请求数据，统一转换为不含原始异常的结果。
            cancellationToken.ThrowIfCancellationRequested();
            return Failure("oss_network_error", Text.Get("CannotConnectToAlibabaCloudOSSCheckYourNetworkAndTryAgain"));
        }
    }

    /// <summary>身份验证只构造固定 GET 请求，Query 不含凭据；读写验证由通用探测器完成。</summary>
    private HttpRequestMessage CreateRequest(string accessKeyId, string accessKeySecret)
    {
        string timestamp = _time.GetUtcNow().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string scope = $"{timestamp[..8]}/{Region}/oss/aliyun_v4_request";
        string canonical = $"GET\n/\nmax-keys=1\nx-oss-content-sha256:UNSIGNED-PAYLOAD\nx-oss-date:{timestamp}\n\n\nUNSIGNED-PAYLOAD";
        string signature = AliyunOssV4Signer.CalculateSignature(canonical, timestamp, Region, accessKeySecret);
        var request = new HttpRequestMessage(HttpMethod.Get, ListBucketsUri);
        request.Headers.Add("x-oss-content-sha256", "UNSIGNED-PAYLOAD");
        request.Headers.Add("x-oss-date", timestamp);
        request.Headers.Date = _time.GetUtcNow();
        request.Headers.TryAddWithoutValidation("Authorization",
            $"OSS4-HMAC-SHA256 Credential={accessKeyId}/{scope},Signature={signature}");
        return request;
    }

    /// <summary>禁止 DTD 和外部实体；不向界面输出服务器返回的消息、名称或身份数据。</summary>
    private static async Task<XDocument?> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength == 0) return null;
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using XmlReader reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
        });
        return await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>只消费已知错误码，不复用 OSS Message 或任何原始异常文本。</summary>
    private static InstanceConnectionTestResult MapFailure(HttpStatusCode status, string? code) => code switch
    {
        "InvalidAccessKeyId" or "SignatureDoesNotMatch" =>
            Failure("oss_authentication_failed", Text.Get("AuthenticationFailedCheckTheAccessKeyIDAndAccessKeySecret")),
        "RequestTimeTooSkewed" => Failure("oss_clock_skew", Text.Get("TheSystemTimeDiffersFromOSSCorrectTheDeviceTimeAndTryAgain")),
        "AccessDenied" => Failure("oss_access_denied", Text.Get("AccessDeniedEnsureTheAccessKeyHasOssListBucketsPermission")),
        _ when status is HttpStatusCode.Unauthorized =>
            Failure("oss_authentication_failed", Text.Get("AuthenticationFailedCheckTheConnectionCredentials")),
        _ when status is HttpStatusCode.Forbidden =>
            Failure("oss_access_denied", Text.Get("AccessDeniedEnsureTheAccessKeyHasOssListBucketsPermission")),
        _ when (int)status >= 500 || status == HttpStatusCode.TooManyRequests =>
            Failure("oss_unavailable", Text.Get("OSSIsTemporarilyUnavailableTryAgainLater")),
        _ => Failure("oss_request_failed", Text.Get("OSSConnectionTestFailedCheckTheConfigurationAndTryAgain")),
    };

    /// <summary>失败不提供伪造的成功检查，也不携带任何请求参数。</summary>
    private static InstanceConnectionTestResult Failure(string code, string message) =>
        new(false, message, code, [new(Text.Get("AuthenticationAndBucketListing"), false)]);
}
