using Asgard.Hermod.Providers.S3;
using Microsoft.Extensions.DependencyInjection;

namespace Asgard.Hermod.Providers.AwsS3;

/// <summary>由宿主显式选择 AwsS3，不隐式安装其他服务商、界面或持久化模块。</summary>
public static class AwsS3ServiceCollectionExtensions
{
    /// <summary>装配独立服务商连接器及其 SDK 传输。</summary>
    public static IServiceCollection AddAwsS3(this IServiceCollection services) => services.AddS3Connector<AwsS3StorageProvider>();
}
