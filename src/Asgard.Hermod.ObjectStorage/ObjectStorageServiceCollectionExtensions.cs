using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.ObjectStorage;

/// <summary>装配对象存储业务与工厂，云 SDK 留在服务商实现中。</summary>
public static class ObjectStorageServiceCollectionExtensions
{
    /// <summary>注册通用资源操作，可配合任意对象存储连接器与配置服务。</summary>
    public static IServiceCollection AddObjectStorage(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IObjectStorageClientFactory, ObjectStorageClientFactory>();
        services.TryAddSingleton<IObjectStorageService, LocalObjectStorageService>();
        return services;
    }
}
