using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.MySql;

/// <summary>宿主自主选择协议与两个连接方法，不依赖 WPF。</summary>
public static class MySqlServiceCollectionExtensions
{
    /// <summary>注册协议会话工厂和连接方法。</summary>
    public static IServiceCollection AddMySql(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(IMySqlSessionFactory))) return services;
        services.TryAddSingleton<IMySqlSessionFactory, MySqlSessionFactory>();
        foreach (string route in new[] { MySqlIds.Direct, MySqlIds.Ssh })
            services.AddSingleton<IInstanceProvider>(provider => new MySqlInstanceProvider(route, provider.GetRequiredService<IMySqlSessionFactory>()));
        return services;
    }
}
