using Asgard.Hermod.MySql.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.MySql.UI;

/// <summary>独立注册 MySQL 的 WPF 表单和工作区。</summary>
public static class MySqlUiServiceCollectionExtensions
{
    public static IServiceCollection AddMySqlUi(this IServiceCollection services, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(owner);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceConfigurationContentProvider, MySqlConfigurationContentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, MySqlPageProvider>());
        services.TryAddSingleton<IMySqlWorkspaceDialogs>(_ => new WindowsMySqlWorkspaceDialogs(owner));
        return services;
    }
}
