using System.Net;
using System.Net.Http;
using System.Text;
using Asgard.Hermod.Providers.AwsS3;
using Asgard.Hermod.Providers.Minio;
using Asgard.Hermod.Providers.S3;
using Asgard.Hermod.Providers.TencentCos;
using Asgard.Hermod.Providers.VolcengineTos;

namespace Asgard.Hermod.UI.Tests;

/// <summary>执行真实 SDK 签名和序列化，仅在 HTTP 边界替换远端服务。</summary>
internal static class S3TestClient
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>所有密钥均为虚构值，不读取机器环境或用户配置。</summary>
    internal static InstanceConnectionOptions Options(string provider = "aws-s3", params (string Key, string Value)[] values)
    {
        var parameters = new Dictionary<string, string>
        {
            ["accessKeyId"] = "SDKTESTACCESSKEY",
            ["accessKeySecret"] = "sdk-private-test-secret",
            ["endpoint"] = "http://localhost:9000",
        };
        foreach (var (key, value) in values) parameters[key] = value;
        return new() { TypeId = "storage", ProviderId = provider, Parameters = parameters };
    }

    /// <summary>经各厂商真实入口构造连接器，不模拟 SDK 客户端本身。</summary>
    internal static S3StorageProvider Provider(StorageTestHttpHandler handler, string provider)
    {
        var factory = new StorageTestHttpClientFactory(handler);
        return provider switch
        {
            "aws-s3" => new AwsS3StorageProvider(factory),
            "tencent-cos" => new TencentCosStorageProvider(factory),
            "volcengine-tos" => new VolcengineTosStorageProvider(factory),
            "minio" => new MinioStorageProvider(factory),
            "s3-compatible" => new S3CompatibleStorageProvider(factory),
            _ => throw new ArgumentOutOfRangeException(nameof(provider)),
        };
    }

    /// <summary>提供真实 S3 XML 响应，SDK 负责反序列化及错误识别。</summary>
    internal static HttpResponseMessage Xml(string xml, HttpStatusCode status = HttpStatusCode.OK) => new(status)
    {
        Content = new StringContent(xml, Encoding.UTF8, "application/xml"),
    };
}
