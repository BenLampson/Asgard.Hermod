using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;

namespace Asgard.Hermod.Agent;

/// <summary>独立注册模型传输和内存配置；不依赖 UI、数据库或设置存储。</summary>
public static class AgentServiceCollectionExtensions
{
    /// <summary>注册用户模型客户端；禁止请求重定向、日志和 POST 自动重试。</summary>
    public static IServiceCollection AddHermodAgent(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IAgentClient))) return services;
        services.TryAddSingleton<AgentConfiguration>();
        services.AddHttpClient<IAgentClient, OpenAiCompatibleAgentClient>(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, ConnectTimeout = TimeSpan.FromSeconds(15) })
            .RemoveAllLoggers()
            .AddStandardResilienceHandler(options =>
            {
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(3);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(6);
                options.Retry.DisableForUnsafeHttpMethods();
            });
        return services;
    }
}
