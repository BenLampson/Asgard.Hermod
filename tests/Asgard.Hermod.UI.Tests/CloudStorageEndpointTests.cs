using System.Net;
using System.Net.Http;
using Asgard.Hermod.ObjectStorage.Abstractions.Models;
using Asgard.Hermod.Providers.AwsS3;
using Asgard.Hermod.Providers.TencentCos;
using Asgard.Hermod.Providers.VolcengineTos;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证各云服务的双输入方式、真实 SDK 签名、独立持久化和编辑恢复。</summary>
public sealed class CloudStorageEndpointTests
{
    /// <summary>每种方式只消费当前输入，隐藏的无效草稿不影响实际签名和请求地址。</summary>
    [Theory]
    [InlineData("aws-s3", "region", "eu-west-1", "s3.eu-west-1.amazonaws.com", "eu-west-1")]
    [InlineData("aws-s3", "endpoint", "https://s3.eu-west-1.amazonaws.com", "s3.eu-west-1.amazonaws.com", "eu-west-1")]
    [InlineData("aws-s3", "endpoint", "s3.cn-north-1.amazonaws.com.cn", "s3.cn-north-1.amazonaws.com.cn", "cn-north-1")]
    [InlineData("aws-s3", "endpoint", "https://s3.amazonaws.com", "s3.amazonaws.com", "us-east-1")]
    [InlineData("tencent-cos", "region", "ap-shanghai", "cos.ap-shanghai.myqcloud.com", "ap-shanghai")]
    [InlineData("tencent-cos", "endpoint", "cos.ap-shanghai.myqcloud.com", "cos.ap-shanghai.myqcloud.com", "ap-shanghai")]
    [InlineData("volcengine-tos", "region", "cn-shanghai", "tos-s3-cn-shanghai.volces.com", "cn-shanghai")]
    [InlineData("volcengine-tos", "endpoint", "https://tos-cn-shanghai.volces.com", "tos-s3-cn-shanghai.volces.com", "cn-shanghai")]
    [InlineData("aliyun-oss", "region", "cn-shanghai", "oss-cn-shanghai.aliyuncs.com", "cn-shanghai")]
    [InlineData("aliyun-oss", "endpoint", "https://oss-cn-shanghai.aliyuncs.com", "oss-cn-shanghai.aliyuncs.com", "cn-shanghai")]
    [InlineData("aliyun-oss", "endpoint", "oss-cn-shanghai-internal.aliyuncs.com", "oss-cn-shanghai-internal.aliyuncs.com", "cn-shanghai")]
    [InlineData("aliyun-oss", "endpoint", "https://cn-shanghai.oss.aliyuncs.com", "cn-shanghai.oss.aliyuncs.com", "cn-shanghai")]
    public async Task ListObjects_UsesSelectedInputAndSigningRegion(string providerId, string mode, string value, string host, string region)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal("test-bucket." + host, request.RequestUri!.Host);
            string signature = request.Headers.GetValues("Authorization").Single();
            Assert.Contains($"/{region}/{(providerId == "aliyun-oss" ? "oss/aliyun_v4_request" : "s3/aws4_request")}", signature);
            return Task.FromResult(S3TestClient.Xml("<ListBucketResult><IsTruncated>false</IsTruncated></ListBucketResult>"));
        });
        var options = Options(providerId, mode, value);
        using var client = ObjectProvider(handler, providerId).CreateClient(options, null);
        await client.ListObjectsAsync("test-bucket", "", null, "/", 10, TestContext.Current.CancellationToken);
        Assert.Equal(1, calls);
        Assert.Equal("inactive-invalid-value/?", options.Parameters[mode == "region" ? "endpoint" : "region"]);
    }

    /// <summary>OSS 身份预检和 SDK 桶探测采用相同输入，不再悄悄使用固定杭州公网地址。</summary>
    [Theory]
    [InlineData("region", "cn-shanghai", "oss-cn-shanghai.aliyuncs.com")]
    [InlineData("endpoint", "https://oss-cn-shanghai-internal.aliyuncs.com", "oss-cn-shanghai-internal.aliyuncs.com")]
    public async Task OssConnectionTest_UsesSelectedEndpointForEveryRequest(string mode, string value, string host)
    {
        int calls = 0;
        using var handler = new StorageTestHttpHandler((request, _) =>
        {
            calls++;
            Assert.Equal(host, request.RequestUri!.Host);
            Assert.Contains("/cn-shanghai/oss/aliyun_v4_request", request.Headers.GetValues("Authorization").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<ListAllMyBucketsResult><Owner><ID>test</ID></Owner><Buckets/></ListAllMyBucketsResult>"),
            });
        });
        var provider = new AliyunOssStorageProvider(new StorageTestHttpClientFactory(handler));
        var result = await provider.TestConnectionAsync(Options("aliyun-oss", mode, value), TestContext.Current.CancellationToken);
        Assert.Equal("oss_probe_no_bucket", result.ErrorCode);
        Assert.Equal(2, calls);
    }

    /// <summary>两种方式及其有效字段随 SQLite 重启恢复，隐藏字段不保存，编辑页恢复原选择。</summary>
    [Theory]
    [InlineData("aws-s3", "region", "eu-west-1")]
    [InlineData("aws-s3", "endpoint", "https://s3.eu-west-1.amazonaws.com")]
    [InlineData("tencent-cos", "region", "ap-shanghai")]
    [InlineData("tencent-cos", "endpoint", "https://cos.ap-shanghai.myqcloud.com")]
    [InlineData("volcengine-tos", "region", "cn-shanghai")]
    [InlineData("volcengine-tos", "endpoint", "https://tos-cn-shanghai.volces.com")]
    [InlineData("aliyun-oss", "region", "cn-shanghai")]
    [InlineData("aliyun-oss", "endpoint", "https://oss-cn-shanghai.aliyuncs.com")]
    public async Task Save_RestoresModeAndActiveInputAfterRestart(string providerId, string mode, string value)
    {
        using var environment = new LocalStorageTestEnvironment();
        ServiceProvider Services() => new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage()
            .AddAliyunOss().AddAwsS3().AddTencentCos().AddVolcengineTos().AddLocalStorage(environment.CreateConfiguration()).BuildServiceProvider();
        using (var services = Services())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var configurations = services.GetRequiredService<IInstanceConfigurationService>();
            await configurations.CreateAsync(new() { Name = "Endpoint choice", Connection = Options(providerId, mode, value) },
                TestContext.Current.CancellationToken);
        }
        using var restarted = Services();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restarted.GetRequiredService<IInstanceConfigurationService>();
        var instance = Assert.Single(restored.GetInstances());
        var saved = await restored.GetConnectionAsync(instance.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal(mode, saved.Parameters["endpointMode"]);
        Assert.Equal(value, saved.Parameters[mode]);
        Assert.False(saved.Parameters.ContainsKey(mode == "region" ? "endpoint" : "region"));
        using var model = new NewInstanceViewModel(restarted.GetRequiredService<IInstanceProviderRegistry>(), restored, instance, saved);
        Assert.Equal(mode, NewInstanceWizardTests.GetField(model, "endpointMode").Value);
        Assert.True(NewInstanceWizardTests.GetField(model, mode).IsVisible);
        Assert.False(NewInstanceWizardTests.GetField(model, mode == "region" ? "endpoint" : "region").IsVisible);
    }

    /// <summary>未知模式和其他厂商地址在网络前安全拒绝，显式选择地址模式不接受单独区域。</summary>
    [Theory]
    [InlineData("aws-s3", "endpoint", "https://cos.ap-shanghai.myqcloud.com", "InvalidEndpoint")]
    [InlineData("tencent-cos", "endpoint", "https://s3.eu-west-1.amazonaws.com", "InvalidEndpoint")]
    [InlineData("volcengine-tos", "endpoint", "cn-shanghai", "InvalidEndpoint")]
    [InlineData("aliyun-oss", "endpoint", "https://oss-cn-shanghai.aliyuncs.com.example.com", "InvalidEndpoint")]
    [InlineData("aws-s3", "invalid", "eu-west-1", "InvalidEndpointMode")]
    public void InvalidInput_FailsBeforeNetwork(string providerId, string mode, string value, string expected)
    {
        using var handler = new StorageTestHttpHandler((_, _) => throw new InvalidOperationException("No network"));
        var error = Assert.Throws<ObjectStorageException>(() => ObjectProvider(handler, providerId).CreateClient(Options(providerId, mode, value), null));
        Assert.Equal(expected, error.ErrorCode);
    }

    /// <summary>旧配置转换不修改原字典；再次转换得到相同的当前格式。</summary>
    [Theory]
    [InlineData("cn-beijing", "region")]
    [InlineData("https://tos-cn-beijing.volces.com", "endpoint")]
    public void LegacyInput_NormalizesWithoutMutation(string legacyValue, string expectedMode)
    {
        var original = S3TestClient.Options("volcengine-tos", ("region", legacyValue));
        var normalized = ObjectStorageEndpointInput.Normalize(original);
        Assert.False(original.Parameters.ContainsKey("endpointMode"));
        Assert.Equal(legacyValue, original.Parameters["region"]);
        Assert.Equal(expectedMode, normalized.Parameters["endpointMode"]);
        Assert.Equal(legacyValue, normalized.Parameters[expectedMode]);
        Assert.Equal(normalized.Parameters, ObjectStorageEndpointInput.Normalize(normalized).Parameters);
    }

    private static InstanceConnectionOptions Options(string providerId, string mode, string value) => new()
    {
        TypeId = "storage",
        ProviderId = providerId,
        Parameters = new Dictionary<string, string>
        {
            ["accessKeyId"] = "TESTACCESSKEY",
            ["accessKeySecret"] = "test-private-secret",
            ["endpointMode"] = mode,
            ["region"] = mode == "region" ? value : "inactive-invalid-value/?",
            ["endpoint"] = mode == "endpoint" ? value : "inactive-invalid-value/?",
        },
    };

    private static IObjectStorageProvider ObjectProvider(StorageTestHttpHandler handler, string id) => id == "aliyun-oss"
        ? new AliyunOssObjectStorageProvider(new StorageTestHttpClientFactory(handler)) : S3TestClient.Provider(handler, id);
}
