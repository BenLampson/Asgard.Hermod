using Asgard.Hermod.PostgreSql.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.PostgreSql.UI;

/// <summary>独立注册 PostgreSQL 的 WPF 表单和工作区。</summary>
public static class PostgreSqlUiServiceCollectionExtensions
{
    /// <summary>注册公共向导正文、工作页和窗口交互服务。</summary>
    public static IServiceCollection AddPostgreSqlUi(this IServiceCollection services, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(owner);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceConfigurationContentProvider, PostgreSqlConfigurationContentProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, PostgreSqlPageProvider>());
        services.TryAddSingleton<IPostgreSqlWorkspaceDialogs>(_ => new WindowsPostgreSqlWorkspaceDialogs(owner));
        return services;
    }
}
