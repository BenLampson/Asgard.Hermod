namespace Asgard.Hermod.Providers.S3;

/// <summary>面向支持 SigV4、条件写入及标准 S3 API 的自定义端点，不推断厂商品牌。</summary>
public sealed class S3CompatibleStorageProvider(IHttpClientFactory clients) : S3StorageProvider(
    new("s3-compatible", "S3CompatibleName", "S3CompatibleDescription", "cloud", "us-east-1",
        _ => "", CustomEndpoint: true, PathStyle: true), clients);
