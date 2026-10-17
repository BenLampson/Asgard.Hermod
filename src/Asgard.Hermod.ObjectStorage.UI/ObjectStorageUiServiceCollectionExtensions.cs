using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.ObjectStorage.UI;

/// <summary>对象存储模块提供自己的工作页和交互服务。</summary>
public static class ObjectStorageUiServiceCollectionExtensions
{
    /// <summary>注册对象存储页面，窗口所有者由宿主提供。</summary>
    public static IServiceCollection AddObjectStorageUi(this IServiceCollection services, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        services.TryAddSingleton<IObjectStorageDialogService>(_ => new WindowsObjectStorageDialogService(owner));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, ObjectStoragePageProvider>());
        return services;
    }
}
