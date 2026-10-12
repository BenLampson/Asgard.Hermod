using Asgard.Hermod.UI.Themes;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.UI;

/// <summary>装配公共桌面交互和工作页工厂，具体功能页面由模块提供。</summary>
public static class HermodUiServiceCollectionExtensions
{
    /// <summary>窗口所有者和主题资源由应用入口传入，UI 不依赖可执行程序。</summary>
    public static IServiceCollection AddHermodUi(this IServiceCollection services,
        Func<Window?> owner, ResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(resources);
        services.TryAddSingleton<IInstanceEditor>(provider => new InstanceEditorService(owner,
            provider.GetRequiredService<IInstanceProviderRegistry>(),
            provider.GetRequiredService<IInstanceConfigurationService>()));
        services.TryAddSingleton<ISettingsEditor>(provider => new SettingsEditorService(owner,
            provider.GetRequiredService<ITagManagementService>(),
            provider.GetRequiredService<ILanguagePreferenceStore>()));
        services.TryAddSingleton<IInstanceWorkspaceFactory, InstanceWorkspaceFactory>();
        services.TryAddSingleton<IThemeService>(_ => new ThemeService(resources));
        return services;
    }
}
