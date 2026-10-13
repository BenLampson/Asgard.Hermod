using Asgard.Hermod.Redis.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Redis.UI;

/// <summary>独立注册 WPF 正文、工作页和模态交互。</summary>
public static class RedisUiServiceCollectionExtensions
{
    /// <summary>共享窗口所属关系由宿主提供。</summary>
    public static IServiceCollection AddRedisUi(this IServiceCollection services, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(owner);
        services.TryAddSingleton<IRedisWorkspaceDialogs>(_ => new WindowsRedisWorkspaceDialogs(owner));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, RedisPageProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceConfigurationContentProvider, RedisConfigurationContentProvider>());
        return services;
    }
}
