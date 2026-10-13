using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Redis;

/// <summary>协议模块注册在宿主中，不依赖界面或本地数据库。</summary>
public static class RedisServiceCollectionExtensions
{
    /// <summary>注册三种拓扑及共用会话工厂。</summary>
    public static IServiceCollection AddRedis(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(IRedisSessionFactory))) return services;
        services.TryAddSingleton<IRedisSessionFactory, RedisSessionFactory>();
        foreach (string topology in new[] { RedisIds.Standalone, RedisIds.Sentinel, RedisIds.Cluster })
            services.AddSingleton<IInstanceProvider>(provider => new RedisInstanceProvider(topology, provider.GetRequiredService<IRedisSessionFactory>()));
        return services;
    }
}
