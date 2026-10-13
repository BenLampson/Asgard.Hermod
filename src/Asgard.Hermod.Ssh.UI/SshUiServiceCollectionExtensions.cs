using Asgard.Hermod.Ssh.UI.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.Ssh.UI;

/// <summary>装配 SSH 信息正文、核验交互和工作页，公共 UI 保持协议无关。</summary>
public static class SshUiServiceCollectionExtensions
{
    /// <summary>协议注册和持久化仍由应用宿主选择。</summary>
    public static IServiceCollection AddSshUi(this IServiceCollection services, Func<Window?> owner)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(owner);
        services.TryAddSingleton<ISshHostKeyPrompt>(_ => new WindowsSshHostKeyPrompt(owner));
        services.TryAddSingleton<ISshWorkspaceDialogs>(_ => new WindowsSshWorkspaceDialogs(owner));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstancePageProvider, SshPageProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IInstanceConfigurationContentProvider, SshConfigurationContentProvider>());
        return services;
    }
}
