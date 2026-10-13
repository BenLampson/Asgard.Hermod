using Asgard.Hermod.Providers.S3;

namespace Asgard.Hermod.Providers.Minio;

/// <summary>Minio 连接器仅声明自身端点和协议差异，业务流程复用公共对象存储模块。</summary>
public sealed class MinioStorageProvider(IHttpClientFactory clients) : S3StorageProvider(
    new("minio", "MinioName", "MinioDescription", "provider-minio", "us-east-1",
        _ => "", CustomEndpoint: true, PathStyle: true), clients);
