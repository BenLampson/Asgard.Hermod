using Asgard.Hermod.Providers.S3;

namespace Asgard.Hermod.Providers.VolcengineTos;

/// <summary>VolcengineTos 连接器仅声明自身端点和协议差异，业务流程复用公共对象存储模块。</summary>
public sealed class VolcengineTosStorageProvider(IHttpClientFactory clients) : S3StorageProvider(
    new("volcengine-tos", "VolcengineTosName", "VolcengineTosDescription", "provider-volcengine-tos", "cn-beijing",
        region => $"https://tos-s3-{region}.volces.com", DiscoverBucketRegion: true, ForbidOverwriteHeader: "x-tos-forbid-overwrite"), clients);
