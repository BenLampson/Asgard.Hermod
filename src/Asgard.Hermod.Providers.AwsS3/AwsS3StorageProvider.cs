using Asgard.Hermod.Providers.S3;

namespace Asgard.Hermod.Providers.AwsS3;

/// <summary>AwsS3 连接器仅声明自身端点和协议差异，业务流程复用公共对象存储模块。</summary>
public sealed class AwsS3StorageProvider(IHttpClientFactory clients) : S3StorageProvider(
    new("aws-s3", "AwsS3Name", "AwsS3Description", "provider-aws-s3", "us-east-1",
        region => $"https://s3.{region}.{(region.StartsWith("cn-", StringComparison.Ordinal) ? "amazonaws.com.cn" : "amazonaws.com")}", DiscoverBucketRegion: true), clients);
