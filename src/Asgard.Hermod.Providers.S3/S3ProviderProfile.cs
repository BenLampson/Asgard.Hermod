namespace Asgard.Hermod.Providers.S3;

/// <summary>只声明服务商协议差异；公共业务和界面无需理解 SDK 类型或厂商标识。</summary>
public sealed record S3ProviderProfile(string Id, string NameResource, string DescriptionResource, string Icon,
    string DefaultRegion, Func<string, string> Endpoint, bool CustomEndpoint = false, bool PathStyle = false,
    bool DiscoverBucketRegion = false, string? ForbidOverwriteHeader = null, S3BucketListDialect? BucketListDialect = null);
