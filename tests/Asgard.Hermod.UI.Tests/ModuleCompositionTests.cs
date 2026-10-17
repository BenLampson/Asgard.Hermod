using Asgard.Hermod.PostgreSql;
using Asgard.Hermod.Providers.AwsS3;
using Asgard.Hermod.Providers.Minio;
using Asgard.Hermod.Providers.S3;
using Asgard.Hermod.Providers.TencentCos;
using Asgard.Hermod.Providers.VolcengineTos;
using Asgard.Hermod.Ssh;

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

    /// <summary>每个云连接器独立装配且可重复调用注册入口，不依赖数据库、界面或其他厂商。</summary>
    [Theory]
    [InlineData("aws-s3")]
    [InlineData("tencent-cos")]
    [InlineData("volcengine-tos")]
    [InlineData("minio")]
    [InlineData("s3-compatible")]
    public void S3Connectors_RegisterIndependentlyAndOnce(string id)
    {
        var registration = new ServiceCollection();
        for (int i = 0; i < 2; i++)
        {
            _ = id switch
            {
                "aws-s3" => registration.AddAwsS3(),
                "tencent-cos" => registration.AddTencentCos(),
                "volcengine-tos" => registration.AddVolcengineTos(),
                "minio" => registration.AddMinio(),
                _ => registration.AddS3Connector<S3CompatibleStorageProvider>(),
            };
        }
        using var services = registration.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var instanceProvider = Assert.Single(services.GetServices<IInstanceProvider>());
        var objectProvider = Assert.Single(services.GetServices<IObjectStorageProvider>());
        Assert.Same(instanceProvider, objectProvider);
        Assert.Equal(id, instanceProvider.Descriptor.Id);
        using var client = objectProvider.CreateClient(S3TestClient.Options(id), null);
        Assert.Null(services.GetService<ILocalConfigurationRepository>());
        Assert.Null(services.GetService<IInstancePageProvider>());
        Assert.Null(services.GetService<IObjectStorageService>());
    }

    /// <summary>PostgreSQL 协议可独立重复装配，直接连接和 SSH 路由各注册一次。</summary>
    [Fact]
    public void PostgreSql_RegistersIndependentlyAndOnce()
    {
        using var services = new ServiceCollection().AddSingleton<ISshTcpStreamFactory>(new UnusedSshTcpStreamFactory())
            .AddPostgreSql().AddPostgreSql().BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        var providers = services.GetServices<IInstanceProvider>().ToArray();
        Assert.Equal(2, providers.Length);
        Assert.All(providers, provider => Assert.IsType<PostgreSqlInstanceProvider>(provider));
        Assert.Equal(new[] { PostgreSqlIds.Direct, PostgreSqlIds.Ssh }, providers.Select(provider => provider.Descriptor.Id));
        Assert.IsType<PostgreSqlSessionFactory>(Assert.Single(services.GetServices<IPostgreSqlSessionFactory>()));
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
    [InlineData(typeof(PostgreSqlSessionFactory))]
    public void PortableModules_DoNotReferenceHostPresentationOrCloudSdk(Type moduleType)
    {
        string[] references = moduleType.Assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

        Assert.DoesNotContain(references, name => name == "Asgard.Hermod" ||
            name.EndsWith(".UI", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.UI", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.ObjectStorage.UI", StringComparison.Ordinal) ||
            name.StartsWith("Asgard.Hermod.Providers.", StringComparison.Ordinal) ||
            name.StartsWith("AlibabaCloud", StringComparison.Ordinal) || name.StartsWith("AWSSDK", StringComparison.Ordinal) ||
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
            name.StartsWith("AlibabaCloud", StringComparison.Ordinal) || name.StartsWith("AWSSDK", StringComparison.Ordinal));
    }

    /// <summary>装配不应提前建立网络连接，SSH 通道由宿主提供。</summary>
    private sealed class UnusedSshTcpStreamFactory : ISshTcpStreamFactory
    {
        public Task<Stream> OpenAsync(InstanceConnectionOptions options, string host, int port, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Module composition must not open connections.");
    }
}
