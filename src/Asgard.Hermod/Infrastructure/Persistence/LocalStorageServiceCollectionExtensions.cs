namespace Asgard.Hermod.Infrastructure.Persistence;

/// <summary>集中装配本地存储，数据库实例的创建和释放由 Asgard 与服务容器负责。</summary>
public static class LocalStorageServiceCollectionExtensions
{
    /// <summary>在数据库首次连接前创建父目录，并注册配置、FreeSql 和结构初始化服务。</summary>
    public static IServiceCollection AddLocalStorage(this IServiceCollection services,
        LocalDatabaseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration.DatabasePath)!);
        services.AddSingleton(configuration);
        services.AddDatabase(configuration.Database);
        services.AddSingleton<LocalDatabaseInitializer>();
        return services;
    }
}
