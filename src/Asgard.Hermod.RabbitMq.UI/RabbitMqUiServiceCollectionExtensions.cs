using Asgard.Hermod.RabbitMq.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.RabbitMq.UI;

/// <summary>宿主只注册公共扩展点，协议和界面不相互依赖。</summary>
public static class RabbitMqUiServiceCollectionExtensions
{
    /// <summary>注册连接表单、实例工作区与共用窗口承载。</summary>
    public static IServiceCollection AddRabbitMqUi(this IServiceCollection services, Func<Window?> owner)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceConfigurationContentProvider, RabbitMqConfigurationContentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, RabbitMqPageProvider>());
        services.TryAddSingleton<IRabbitMqDialogs>(_ => new RabbitMqDialogs(owner));
        return services;
    }
}
