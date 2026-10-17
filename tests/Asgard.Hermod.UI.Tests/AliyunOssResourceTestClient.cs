
namespace Asgard.Hermod.UI.Tests;

/// <summary>经生产资源工厂创建官方 SDK，仅将 HTTP 传输替换为可控处理器。</summary>
public static class AliyunOssResourceTestClient
{
    /// <summary>所有 SDK 边界测试使用虚构参数，合法区域仍构造真实官方域名。</summary>
    public static IObjectStorageClient Create(StorageTestHttpHandler handler, string region = "cn-hangzhou") =>
        new AliyunOssObjectStorageProvider(new StorageTestHttpClientFactory(handler)).CreateClient(Options(), region);

    /// <summary>安全断言可以识别 SDK 异常正文中的虚构密钥字符串。</summary>
    public static InstanceConnectionOptions Options() => new()
    {
        TypeId = "storage",
        ProviderId = "aliyun-oss",
        Parameters = new Dictionary<string, string> { ["accessKeyId"] = "LTAIResourceTestKey", ["accessKeySecret"] = "resource-private-secret" },
    };
}
