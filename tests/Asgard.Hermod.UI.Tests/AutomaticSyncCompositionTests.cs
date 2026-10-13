using Asgard.Hermod.Identity;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实桌面模块装配验证装饰器依赖、离线保存和账户持久化隔离。</summary>
public sealed class AutomaticSyncCompositionTests
{
    /// <summary>Identity 装饰 SQLite 服务仍可在未登录时保存，不触发网络调用。</summary>
    [Fact]
    public async Task DesktopComposition_SavesLocallyWithoutLoginOrNetwork()
    {
        using var environment = new LocalStorageTestEnvironment();
        var services = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss()
            .AddLocalStorage(environment.CreateConfiguration()).AddHermodIdentity(new()
            {
                Authority = "https://issuer.test/tenant",
                ApiBaseUrl = "https://sync.test",
                ClientId = "desktop-client",
                TenantId = Guid.NewGuid().ToString(),
                ApplicationId = Guid.NewGuid().ToString(),
            });
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        provider.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configurations = provider.GetRequiredService<IInstanceConfigurationService>(); Assert.IsType<SyncedInstanceConfigurationService>(configurations);
        var instance = await configurations.CreateAsync(new()
        {
            Name = "Offline configuration",
            Connection = new()
            {
                TypeId = AliyunOssStorageProvider.TypeId,
                ProviderId = AliyunOssStorageProvider.ProviderId,
                Parameters = new Dictionary<string, string> { ["accessKeyId"] = "LocalTestKey123", ["accessKeySecret"] = "local-test-secret" }
            },
        }, TestContext.Current.CancellationToken);
        Assert.Contains(configurations.GetInstances(), value => value.Id == instance.Id);
        Assert.Equal(ConfigurationSyncStatus.Local, provider.GetRequiredService<IConfigurationSyncService>().Status);
        Assert.IsType<SyncedTagManagementService>(provider.GetRequiredService<ITagManagementService>());
        await provider.GetRequiredService<ITagManagementService>().SaveAsync(null, new("Local tag", null, "#123456"), TestContext.Current.CancellationToken);
        Assert.Null(provider.GetRequiredService<IAccountService>().Account);
    }
}
