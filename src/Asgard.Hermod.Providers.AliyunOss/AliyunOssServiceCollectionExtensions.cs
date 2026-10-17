using System.Net.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Asgard.Hermod.Providers.AliyunOss;

/// <summary>阿里云模块自行注册连接器及 SDK 专属 HTTP 策略。</summary>
public static class AliyunOssServiceCollectionExtensions
{
    /// <summary>只装配阿里云实现，不引入持久化、桌面界面或其他服务商。</summary>
    public static IServiceCollection AddAliyunOss(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceProvider, AliyunOssStorageProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IObjectStorageProvider, AliyunOssObjectStorageProvider>());
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
