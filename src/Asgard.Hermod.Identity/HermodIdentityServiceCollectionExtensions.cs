using Microsoft.Extensions.Http.Resilience;

namespace Asgard.Hermod.Identity;

/// <summary>协议客户端和同步 HTTP 不依赖 WPF 或 SQLite 实现。</summary>
public static class HermodIdentityServiceCollectionExtensions
{
    /// <summary>所有网络调用禁止重定向携带凭据和自动重试非安全写操作。</summary>
    public static IServiceCollection AddHermodIdentity(this IServiceCollection services, HermodIdentityOptions options)
    {
        options.Validate();
        services.AddSingleton(options);
        services.AddSingleton(_ => new HeimdallServerClock(TimeProvider.System));
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<HeimdallServerClock>());
        services.AddOptions<HermodIdentityOptions>().Configure(value =>
        {
            value.Authority = options.Authority; value.ClientId = options.ClientId; value.TenantId = options.TenantId;
            value.ApplicationId = options.ApplicationId; value.Audience = options.Audience; value.Scope = options.Scope;
            value.RedirectUri = options.RedirectUri; value.ApiBaseUrl = options.ApiBaseUrl;
            value.ApiServerAddress = options.ApiServerAddress;
        }).Validate(value => { value.Validate(); return true; }).ValidateOnStart();
        services.AddHttpClient("hermod.oauth", client => client.Timeout = TimeSpan.FromSeconds(20))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            .AddHttpMessageHandler(provider => new HeimdallClockHandler(provider.GetRequiredService<HeimdallServerClock>(), options))
            .AddStandardResilienceHandler(value => value.Retry.DisableForUnsafeHttpMethods());
        services.AddSingleton(provider => new HeimdallOAuthClient(options.ToOAuthOptions(),
            httpClient: provider.GetRequiredService<IHttpClientFactory>().CreateClient("hermod.oauth"),
            timeProvider: provider.GetRequiredService<HeimdallServerClock>()));
        services.AddSingleton<ILoginBrowser, LoopbackLoginBrowser>();
        services.AddSingleton<IAccountService, HeimdallAccountService>();
        services.AddHttpClient("hermod.sync", client =>
        {
            client.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => HermodApiTransport.CreateHandler(options))
            .AddStandardResilienceHandler(value => value.Retry.DisableForUnsafeHttpMethods());
        services.AddSingleton<IWorkspaceSyncService, WorkspaceSyncService>();
        services.AddSingleton<IConfigurationSyncService, ConfigurationSyncService>();
        // 装饰现有本地服务，Identity 不引用 SQLite 实现类型。
        var local = services.LastOrDefault(service => service.ServiceType == typeof(IInstanceConfigurationService));
        if (local is not null)
        {
            services.Remove(local);
            services.AddSingleton<IInstanceConfigurationService>(provider => new SyncedInstanceConfigurationService(
                (IInstanceConfigurationService)(local.ImplementationInstance ?? local.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, local.ImplementationType!)), provider.GetRequiredService<IConfigurationSyncService>(), provider.GetRequiredService<IAccountService>()));
        }
        var localTags = services.LastOrDefault(service => service.ServiceType == typeof(ITagManagementService));
        if (localTags is not null)
        {
            services.Remove(localTags);
            services.AddSingleton<ITagManagementService>(provider => new SyncedTagManagementService(
                (ITagManagementService)(localTags.ImplementationInstance ?? localTags.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, localTags.ImplementationType!)), provider.GetRequiredService<IConfigurationSyncService>()));
        }
        return services;
    }
}
