using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Core;

/// <summary>装配通用实例注册表和工作区数据源，不依赖具体连接器。</summary>
public static class HermodCoreServiceCollectionExtensions
{
    /// <summary>通过通用契约组合实例能力，连接器由独立模块注册。</summary>
    public static IServiceCollection AddHermodCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IInstanceProviderRegistry, InstanceProviderRegistry>();
        services.TryAddSingleton<IWorkspaceDataSource, LocalWorkspaceDataSource>();
        return services;
    }
}
