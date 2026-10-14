using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>只装配 SQLite 持久化，连接器与凭据保护实现由宿主选择。</summary>
public static class LocalStorageServiceCollectionExtensions
{
    /// <summary>注册数据库、迁移、仓储和本地配置服务，不隐式安装第三方连接器。</summary>
    public static IServiceCollection AddLocalStorage(this IServiceCollection services,
        LocalDatabaseConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        Directory.CreateDirectory(Path.GetDirectoryName(configuration.DatabasePath)!);
        services.AddSingleton(configuration);
        services.AddDatabase(configuration.Database);
        services.AddSingleton<LocalDatabaseInitializer>();
        services.TryAddSingleton<ILocalConfigurationRepository, SqliteConfigurationRepository>();
        services.TryAddSingleton<IInstanceConfigurationService, LocalInstanceConfigurationService>();
        services.TryAddSingleton<LocalTagManagementService>();
        services.TryAddSingleton<ITagManagementService>(provider => provider.GetRequiredService<LocalTagManagementService>());
        services.TryAddSingleton<ILanguagePreferenceStore, SqliteLanguagePreferenceStore>();
        services.TryAddSingleton<ITrayPreferenceStore, SqliteTrayPreferenceStore>();
        services.TryAddSingleton<IMainWindowClosePreferenceStore, SqliteMainWindowClosePreferenceStore>();
        services.TryAddSingleton<IHostKeyTrustStore, SqliteHostKeyTrustStore>();
        services.TryAddSingleton<IRecentInstanceStore, SqliteRecentInstanceStore>();
        services.TryAddSingleton<IFileOpenRuleStore, SqliteFileOpenRuleStore>();
        services.TryAddSingleton<IAccountSessionStore, SqliteAccountSessionStore>();
        services.TryAddSingleton<IConfigurationSyncStateStore, SqliteConfigurationSyncStateStore>();
        services.TryAddSingleton<ILocalWorkspaceStore, SqliteWorkspaceStore>();
        return services;
    }
}
