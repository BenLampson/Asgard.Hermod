namespace Asgard.Hermod.UI.Tests;

/// <summary>验证基础模块可独立组合，持久化不会隐式安装远程连接器。</summary>
public sealed class ModuleCompositionTests
{
    /// <summary>只装配本地能力时能初始化数据库，且容器中不存在云 SDK 或资源服务。</summary>
    [Fact]
    public void LocalStorage_InitializesWithoutObjectStorageOrCloudProviders()
    {
        using var environment = new LocalStorageTestEnvironment();
        var registration = new ServiceCollection()
            .AddHermodCore()
            .AddWindowsSecretProtection()
            .AddLocalStorage(environment.CreateConfiguration());
        using var services = registration.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        Assert.Empty(services.GetRequiredService<IInstanceProviderRegistry>().Types);
        Assert.Empty(services.GetRequiredService<IInstanceConfigurationService>().GetInstances());
        Assert.Empty(services.GetServices<IInstanceProvider>());
        Assert.Empty(services.GetServices<IObjectStorageProvider>());
        Assert.Null(services.GetService<IObjectStorageService>());
        Assert.Null(services.GetService<IObjectStorageClientFactory>());
        Assert.Null(services.GetService<System.Net.Http.IHttpClientFactory>());
        Assert.True(File.Exists(services.GetRequiredService<LocalDatabaseConfiguration>().DatabasePath));
    }

    /// <summary>阿里云模块可独立注册连接器，真实 SDK 构造不要求数据库或 WPF。</summary>
    [Fact]
    public void AliyunOss_RegistersConnectorsWithoutPersistenceOrPresentation()
    {
        using var services = new ServiceCollection().AddAliyunOss().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.IsType<AliyunOssStorageProvider>(Assert.Single(services.GetServices<IInstanceProvider>()));
        var provider = Assert.IsType<AliyunOssObjectStorageProvider>(Assert.Single(services.GetServices<IObjectStorageProvider>()));
        using var client = provider.CreateClient(AliyunOssResourceTestClient.Options(), "cn-beijing");

        Assert.IsType<AliyunOssObjectStorageClient>(client);
        Assert.Null(services.GetService<ILocalConfigurationRepository>());
        Assert.Null(services.GetService<IInstanceConfigurationService>());
        Assert.Null(services.GetService<IInstancePageProvider>());
    }

    /// <summary>业务及契约程序集不得反向引用宿主、WPF 或服务商实现。</summary>
    [Theory]
    [InlineData(typeof(InstanceSummary))]
    [InlineData(typeof(InstanceProviderRegistry))]
    [InlineData(typeof(LocalDatabaseInitializer))]
    [InlineData(typeof(IObjectStorageClient))]
    [InlineData(typeof(LocalObjectStorageService))]
    public void PortableModules_DoNotReferenceHostPresentationOrCloudSdk(Type moduleType)
    {
        string[] references = moduleType.Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        Assert.DoesNotContain(references, name => name == "Asgard.Hermod" ||
            name.StartsWith("Asgard.Hermod.UI", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.ObjectStorage.UI", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.Providers.", StringComparison.Ordinal) ||
            name.StartsWith("AlibabaCloud", StringComparison.Ordinal) ||
            name is "PresentationFramework" or "PresentationCore" or "WindowsBase");
    }

    /// <summary>公共界面只引用公共契约及终端，功能界面和第三方实现独立发布。</summary>
    [Fact]
    public void SharedUi_DoesNotReferenceObjectStoragePersistenceOrCloudSdk()
    {
        string[] references = typeof(WorkspaceContentHost).Assembly.GetReferencedAssemblies()
            .Select(name => name.Name!).ToArray();

        Assert.DoesNotContain(references, name => name == "Asgard.Hermod" ||
            name.StartsWith("Asgard.Hermod.ObjectStorage", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.Persistence", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.Providers", StringComparison.Ordinal) ||
            name.StartsWith("AlibabaCloud", StringComparison.Ordinal));
    }
}
