using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.S3;

namespace Asgard.Hermod.Providers.S3;

/// <summary>官方 SDK 扩展点只适配服务商列桶参数和条件写入头，签名、序列化仍交给 SDK。</summary>
internal sealed class S3SdkClient : AmazonS3Client
{
    /// <summary>处理器在签名前运行，厂商自定义头也进入签名。</summary>
    internal S3SdkClient(AWSCredentials credentials, AmazonS3Config config, S3ProviderProfile profile) : base(credentials, config)
    {
        if (profile.ForbidOverwriteHeader is { } header)
            RuntimePipeline.AddHandlerBefore<Signer>(new S3ConditionalWriteHandler(header));
        if (profile.BucketListDialect is { } dialect)
            RuntimePipeline.AddHandlerBefore<Signer>(new S3BucketListRequestHandler(dialect));
    }
}
