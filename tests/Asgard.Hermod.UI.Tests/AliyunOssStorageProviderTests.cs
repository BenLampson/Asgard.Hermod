using System.Net;
using System.Net.Http;

namespace Asgard.Hermod.UI.Tests;

/// <summary>以官方向量和 HTTP 边界替身验证 OSS V4 身份请求、读写要求、失败脱敏及取消。</summary>
public sealed class AliyunOssStorageProviderTests
{
    /// <summary>使用官方 C# SDK 测试的完整 ak/sk 向量，避免把文档中的占位密钥视为真实向量。</summary>
    /// <remarks>来源：https://github.com/aliyun/alibabacloud-oss-csharp-sdk-v2/blob/master/test/AlibabaCloud.OSS.V2.UnitTests/Signer/SignerV4Test.cs 。</remarks>
    [Fact]
    public void Signature_MatchesOfficialVersionFourVector()
    {
        const string canonical = "PUT\n/bucket/1234%2B-/123/1.txt\n" +
            "%2Bparam1=value3&%2Bparam2&%7Cparam1=value4&%7Cparam2&param1=value1&param2\n" +
            "content-type:text/plain\nx-oss-content-sha256:UNSIGNED-PAYLOAD\n" +
            "x-oss-date:20231216T162057Z\nx-oss-head1:value\n\n\nUNSIGNED-PAYLOAD";

        string signature = AliyunOssV4Signer.CalculateSignature(canonical, "20231216T162057Z", "cn-hangzhou", "sk");

        Assert.Equal("e21d18daa82167720f9b1047ae7e7f1ce7cb77a31e8203a7d5f4624fa0284afe", signature);
    }

    /// <summary>身份请求仍使用已验证的 V4 签名；空容器列表不能冒充完整读写连接成功。</summary>
    [Fact]
    public async Task TestConnectionAsync_RequiresWritableBucketAfterSignedIdentityCheck()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            Interlocked.Increment(ref calls);
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.DoesNotContain("test-private", request.RequestUri?.AbsoluteUri ?? "");
            Assert.Null(request.Content);
            if (calls == 1)
            {
                Assert.Equal("https://oss-cn-hangzhou.aliyuncs.com/?max-keys=1", request.RequestUri?.AbsoluteUri);
                Assert.Equal("20250411T064124Z", Assert.Single(request.Headers.GetValues("x-oss-date")));
                Assert.Equal("UNSIGNED-PAYLOAD", Assert.Single(request.Headers.GetValues("x-oss-content-sha256")));
                string authorization = Assert.Single(request.Headers.GetValues("Authorization"));
                Assert.Equal("OSS4-HMAC-SHA256 Credential=LTAITestPrivateKey/20250411/cn-hangzhou/oss/aliyun_v4_request," +
                    "Signature=9f743cb3c5e0b1dd478a49453187a768221fa1012a2a3d0bc8d1df4cfa4853dc", authorization);
                Assert.DoesNotContain("test-private-secret", authorization);
            }
            else Assert.Contains("max-keys=100", request.RequestUri?.Query);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<ListAllMyBucketsResult><Owner><ID>private-owner</ID></Owner><Buckets /></ListAllMyBucketsResult>"),
            });
        });
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler), new FixedStorageTestTimeProvider());

        InstanceConnectionTestResult result = await provider.TestConnectionAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_probe_no_bucket", result.ErrorCode);
        Assert.Equal(2, calls);
        Assert.NotNull(result.Checks);
        Assert.Contains(result.Checks, check => !check.IsSuccessful);
        Assert.DoesNotContain("private-owner", result.Message);
    }

    /// <summary>第三方响应中的消息和身份数据不能进入界面，只映射白名单状态。</summary>
    [Theory]
    [InlineData("AccessDenied", "oss_access_denied")]
    [InlineData("InvalidAccessKeyId", "oss_authentication_failed")]
    [InlineData("SignatureDoesNotMatch", "oss_authentication_failed")]
    [InlineData("RequestTimeTooSkewed", "oss_clock_skew")]
    [InlineData("private-untrusted-code", "oss_access_denied")]
    public async Task TestConnectionAsync_SanitizesErrorResponses(string code, string expectedCode)
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent($"<Error><Code>{code}</Code><Message>test-private-secret LTAITestPrivateKey</Message></Error>"),
        }));
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));

        InstanceConnectionTestResult result = await provider.TestConnectionAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal(expectedCode, result.ErrorCode);
        Assert.DoesNotContain("test-private-secret", result.ToString());
        Assert.DoesNotContain("LTAITestPrivateKey", result.ToString());
        Assert.DoesNotContain("private-untrusted-code", result.ToString());
    }

    /// <summary>网络异常可能包含密钥文本，结果只能返回固定的安全错误。</summary>
    [Fact]
    public async Task TestConnectionAsync_SanitizesNetworkFailure()
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new HttpRequestException("test-private-secret LTAITestPrivateKey"));
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));

        InstanceConnectionTestResult result = await provider.TestConnectionAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_network_error", result.ErrorCode);
        Assert.DoesNotContain("test-private-secret", result.ToString());
        Assert.DoesNotContain("LTAITestPrivateKey", result.ToString());
    }

    /// <summary>HTTP 成功但响应格式错误，不能伪造认证与权限成功。</summary>
    [Theory]
    [InlineData("<Error><Code>AccessDenied</Code></Error>")]
    [InlineData("<ListAllMyBucketsResult />")]
    [InlineData("not xml")]
    [InlineData("<!DOCTYPE test [<!ENTITY secret SYSTEM 'file:///private'>]><ListAllMyBucketsResult>&secret;</ListAllMyBucketsResult>")]
    public async Task TestConnectionAsync_RejectsUnverifiableSuccessBody(string body)
    {
        using var handler = new StorageTestHttpHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body),
        }));
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));

        InstanceConnectionTestResult result = await provider.TestConnectionAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_invalid_response", result.ErrorCode);
    }

    /// <summary>测试期限中止真实等待请求，并返回固定中文超时结果。</summary>
    [Fact]
    public async Task TestConnectionAsync_CancelsHttpRequestAtDeadline()
    {
        using var handler = new StorageTestHttpHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler), timeout: TimeSpan.FromMilliseconds(30));

        InstanceConnectionTestResult result = await provider.TestConnectionAsync(CreateOptions(), TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("oss_timeout", result.ErrorCode);
    }

    /// <summary>调用方取消原样传播，不能被第三方失败结果吞掉。</summary>
    [Fact]
    public async Task TestConnectionAsync_PropagatesCallerCancellation()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StorageTestHttpHandler(async (_, cancellationToken) =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Task<InstanceConnectionTestResult> pending = provider.TestConnectionAsync(CreateOptions(), cancellation.Token);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    /// <summary>错误服务商和缺失密钥在网络边界前拒绝，避免凭据流入错误连接器。</summary>
    [Fact]
    public async Task TestConnectionAsync_RejectsMismatchedProviderBeforeHttp()
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));
        InstanceConnectionTestResult result = await provider.TestConnectionAsync(new InstanceConnectionOptions
        {
            TypeId = AliyunOssStorageProvider.TypeId,
            ProviderId = "other-provider",
            Parameters = CreateOptions().Parameters,
        }, TestContext.Current.CancellationToken);
        InstanceConnectionTestResult invalid = await provider.TestConnectionAsync(new InstanceConnectionOptions
        {
            TypeId = AliyunOssStorageProvider.TypeId,
            ProviderId = AliyunOssStorageProvider.ProviderId,
            Parameters = new Dictionary<string, string>(),
        }, TestContext.Current.CancellationToken);

        Assert.False(result.IsSuccessful);
        Assert.Equal("instance_provider_mismatch", result.ErrorCode);
        Assert.Equal("oss_invalid_credentials", invalid.ErrorCode);
        InstanceConnectionTestResult wrongType = await provider.TestConnectionAsync(new InstanceConnectionOptions
        {
            TypeId = "docker",
            ProviderId = AliyunOssStorageProvider.ProviderId,
            Parameters = CreateOptions().Parameters,
        }, TestContext.Current.CancellationToken);
        Assert.Equal("instance_provider_mismatch", wrongType.ErrorCode);
        Assert.Equal(0, calls);
    }

    /// <summary>所有测试使用虚构凭据；除专门签名断言外不输出其内容。</summary>
    private static InstanceConnectionOptions CreateOptions() => new()
    {
        TypeId = AliyunOssStorageProvider.TypeId,
        ProviderId = AliyunOssStorageProvider.ProviderId,
        Parameters = new Dictionary<string, string>
        {
            ["accessKeyId"] = "LTAITestPrivateKey",
            ["accessKeySecret"] = "test-private-secret",
        },
    };
}
