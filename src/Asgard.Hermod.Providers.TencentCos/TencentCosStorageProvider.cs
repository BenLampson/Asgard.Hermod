using Asgard.Hermod.Providers.S3;

namespace Asgard.Hermod.Providers.TencentCos;

/// <summary>TencentCos 连接器仅声明自身端点和协议差异，业务流程复用公共对象存储模块。</summary>
public sealed class TencentCosStorageProvider(IHttpClientFactory clients) : S3StorageProvider(
    new("tencent-cos", "TencentCosName", "TencentCosDescription", "provider-tencent-cos", "ap-guangzhou",
        region => $"https://cos.{region}.myqcloud.com", ForbidOverwriteHeader: "x-cos-forbid-overwrite",
        BucketListDialect: new("Location", "NextMarker", "marker", "max-keys"))
    {
        ResolveEndpoint = (value, region) => S3RegionalEndpoint.Resolve(value, ["cos."], [".myqcloud.com"], "cos.", region),
    }, clients);
