using System.Net.Http;
using Asgard.Hermod.Infrastructure.Instances;
using Asgard.Hermod.Infrastructure.Security;
using Asgard.Hermod.Infrastructure.Storage;
using Asgard.Hermod.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Asgard.Hermod.Infrastructure.Persistence;

/// <summary>集中装配本地存储，数据库实例的创建和释放由 Asgard 与服务容器负责。</summary>
public static class LocalStorageServiceCollectionExtensions
{
    /// <summary>在数据库首次连接前创建父目录，并注册配置、FreeSql 和结构初始化服务。</summary>
    public static IServiceCollection AddLocalStorage(this IServiceCollection services,
        LocalDatabaseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration.DatabasePath)!);
        services.AddSingleton(configuration);
        services.AddDatabase(configuration.Database);
        services.AddSingleton<LocalDatabaseInitializer>();
        services.TryAddSingleton<ISecretProtector, WindowsDpapiSecretProtector>();
        services.TryAddSingleton<ILocalConfigurationRepository, SqliteConfigurationRepository>();
        services.TryAddSingleton<IInstanceProviderRegistry, InstanceProviderRegistry>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceProvider, AliyunOssStorageProvider>());
        services.TryAddSingleton<IInstanceConfigurationService, LocalInstanceConfigurationService>();
        services.TryAddSingleton<ITagManagementService, LocalTagManagementService>();
        services.TryAddSingleton<IWorkspaceDataSource, LocalWorkspaceDataSource>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStorageProvider, AliyunOssObjectStorageProvider>());
        services.TryAddSingleton<IObjectStorageClientFactory, ObjectStorageClientFactory>();
        services.TryAddSingleton<IObjectStorageService, LocalObjectStorageService>();
        services.AddHttpClient(AliyunOssStorageProvider.HttpClientName, client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            })
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(10);
                options.Retry.DisableForUnsafeHttpMethods();
            });
        services.AddHttpClient(AliyunOssObjectStorageProvider.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                ConnectTimeout = TimeSpan.FromSeconds(10),
            })
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(options =>
            {
                // 传输大文件保留一小时请求期限，列表和元数据另由 SDK 设置二十秒期限。
                options.TotalRequestTimeout.Timeout = TimeSpan.FromHours(1);
                options.AttemptTimeout.Timeout = TimeSpan.FromHours(1);
                // 标准策略要求熔断采样至少覆盖两次请求期限，客户端创建时也会验证此约束。
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromHours(2);
                options.Retry.DisableForUnsafeHttpMethods();
            });
        return services;
    }
}
