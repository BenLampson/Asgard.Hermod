using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Ssh;

/// <summary>注册 SSH 协议；设备信任和交互由宿主选择。</summary>
public static class SshServiceCollectionExtensions
{
    /// <summary>注册三种路径，不引用 WPF、SQLite 或可执行程序。</summary>
    public static IServiceCollection AddSsh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(item => item.ServiceType == typeof(SshModuleRegistration))) return services;
        services.AddSingleton(new SshModuleRegistration());
        services.TryAddSingleton<ISshConnectionFactory, SshConnectionFactory>();
        foreach (string route in new[] { SshIds.Direct, SshIds.Jump, SshIds.Proxy })
            services.AddSingleton<IInstanceProvider>(provider => new SshInstanceProvider(route, provider.GetRequiredService<ISshConnectionFactory>()));
        return services;
    }
}
