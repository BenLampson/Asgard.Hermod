using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Security.Windows;

/// <summary>隔离 Windows 当前用户凭据保护实现。</summary>
public static class WindowsSecretProtectionServiceCollectionExtensions
{
    /// <summary>由宿主明确选择 DPAPI，持久化模块只依赖凭据保护契约。</summary>
    public static IServiceCollection AddWindowsSecretProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ISecretProtector, WindowsDpapiSecretProtector>();
        return services;
    }
}
