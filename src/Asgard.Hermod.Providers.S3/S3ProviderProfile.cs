namespace Asgard.Hermod.Providers.S3;

/// <summary>只声明服务商协议差异；公共业务和界面无需理解 SDK 类型或厂商标识。</summary>
public sealed record S3ProviderProfile(string Id, string NameResource, string DescriptionResource, string Icon,
    string DefaultRegion, Func<string, string> Endpoint, bool CustomEndpoint = false, bool PathStyle = false,
    bool DiscoverBucketRegion = false, string? ForbidOverwriteHeader = null, S3BucketListDialect? BucketListDialect = null)
{
    /// <summary>完整地址模式的厂商说明；未指定时显示含默认端点示例的共享说明。</summary>
    public string? EndpointHintResource { get; init; }

    /// <summary>解析厂商地址及签名区域；可选区域覆盖来自桶发现结果。</summary>
    public Func<string, string?, (string Region, string Endpoint)>? ResolveEndpoint { get; init; }
}
