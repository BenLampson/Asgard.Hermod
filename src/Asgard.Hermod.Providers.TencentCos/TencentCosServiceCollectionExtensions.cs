using Asgard.Hermod.Providers.S3;
using Microsoft.Extensions.DependencyInjection;

namespace Asgard.Hermod.Providers.TencentCos;

/// <summary>由宿主显式选择 TencentCos，不隐式安装其他服务商、界面或持久化模块。</summary>
public static class TencentCosServiceCollectionExtensions
{
    /// <summary>装配独立服务商连接器及其 SDK 传输。</summary>
    public static IServiceCollection AddTencentCos(this IServiceCollection services) => services.AddS3Connector<TencentCosStorageProvider>();
}
