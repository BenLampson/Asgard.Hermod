using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Asgard.Hermod.Providers.S3;

/// <summary>只安装 S3 协议传输及指定连接器，注册不依赖持久化或 WPF。</summary>
public static class S3ServiceCollectionExtensions
{
    /// <summary>所有 S3 SDK 客户端共用的传输池名称，不包含任何账户信息。</summary>
    public const string HttpClientName = "Hermod.S3.Resources";

    /// <summary>连接器只注册一次；HTTP 弹性策略也只装配一次，避免重试层数叠加。</summary>
    public static IServiceCollection AddS3Connector<T>(this IServiceCollection services) where T : S3StorageProvider
    {
        if (services.Any(item => item.ServiceType == typeof(T))) return services;
        if (!services.Any(item => item.ServiceType == typeof(S3HttpClientFactory)))
        {
            services.TryAddSingleton<S3HttpClientFactory>();
            services.AddHttpClient(HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                {
                    AllowAutoRedirect = false,
                    ConnectTimeout = TimeSpan.FromSeconds(10),
                })
                .RemoveAllLoggers()
                .AddStandardResilienceHandler(options =>
                {
                    options.TotalRequestTimeout.Timeout = TimeSpan.FromHours(1);
                    options.AttemptTimeout.Timeout = TimeSpan.FromHours(1);
                    options.CircuitBreaker.SamplingDuration = TimeSpan.FromHours(2);
                    options.Retry.DisableForUnsafeHttpMethods();
                });
        }
        services.AddSingleton<T>();
        services.AddSingleton<IInstanceProvider>(provider => provider.GetRequiredService<T>());
        services.AddSingleton<IObjectStorageProvider>(provider => provider.GetRequiredService<T>());
        return services;
    }
}
