using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace Asgard.Hermod.RabbitMq;

/// <summary>注册隔离会话和两种连接路径。</summary>
public static class RabbitMqServiceCollectionExtensions
{
    /// <summary>所有请求均只发送一次，包含 GET 消息所用的 POST。</summary>
    public static IServiceCollection AddRabbitMq(this IServiceCollection services)
    {
        if (services.Any(item => item.ServiceType == typeof(IRabbitMqSessionFactory))) return services;
        services.AddHttpClient(RabbitMqIds.HttpClient, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new RabbitMqRoutingHandler()).RemoveAllLoggers()
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.ShouldHandle = _ => ValueTask.FromResult(false);
                options.CircuitBreaker.ShouldHandle = _ => ValueTask.FromResult(false);
                options.AttemptTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(6);
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromMinutes(12);
            });
        services.AddSingleton<IRabbitMqSessionFactory, RabbitMqSessionFactory>();
        foreach (string route in new[] { RabbitMqIds.Direct, RabbitMqIds.Ssh })
            services.AddSingleton<IInstanceProvider>(provider => new RabbitMqInstanceProvider(route, provider.GetRequiredService<IRabbitMqSessionFactory>()));
        return services;
    }
}
