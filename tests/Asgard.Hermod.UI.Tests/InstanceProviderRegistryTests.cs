using Asgard.Hermod.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证类型与服务商组合隔离、第三方字段以及非存储配置的真实持久化。</summary>
public sealed class InstanceProviderRegistryTests
{
    /// <summary>不同类型允许相同服务商标识，但查找与连接不能跨类型转交配置。</summary>
    [Fact]
    public async Task Registry_IsolatesProvidersByTypeAndStableId()
    {
        var first = new FakeStorageProvider("first");
        var storage = new FakeStorageProvider("shared");
        FakeInstanceProvider docker = CreateDockerProvider("shared");
        var registry = new InstanceProviderRegistry([first, storage, docker]);
        var options = new InstanceConnectionOptions
        {
            TypeId = "docker",
            ProviderId = "shared",
            Parameters = new Dictionary<string, string> { ["engineToken"] = "private" },
        };

        InstanceConnectionTestResult result = await registry.GetProvider("docker", "shared")
            .TestConnectionAsync(options, TestContext.Current.CancellationToken);

        Assert.True(result.IsSuccessful);
        Assert.Null(first.ReceivedOptions);
        Assert.Null(storage.ReceivedOptions);
        Assert.Same(options, docker.ReceivedOptions);
        Assert.Equal(2, registry.Types.Count);
        Assert.Equal(2, registry.GetProviders("storage").Count);
        Assert.Single(registry.GetProviders("docker"));
        Assert.Same(storage, registry.GetProvider("storage", "shared"));
        Assert.Throws<InvalidOperationException>(() => registry.GetProvider("Docker", "shared"));
        Assert.Throws<InvalidOperationException>(() => registry.GetProvider("docker", "SHARED"));
        Assert.Throws<InvalidOperationException>(() => registry.GetProvider("docker", "first"));
        Assert.Throws<InvalidOperationException>(() => registry.GetProviders("unknown"));
        InstanceConnectionTestResult mismatch = await storage.TestConnectionAsync(options, TestContext.Current.CancellationToken);
        Assert.False(mismatch.IsSuccessful);
        Assert.Null(storage.ReceivedOptions);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => docker.TestConnectionAsync(options, cancellation.Token));
    }

    /// <summary>同类型重复服务商、不一致类型元数据和非法字段在启动阶段明确拒绝。</summary>
    [Fact]
    public void Registry_RejectsConflictingMetadataAndFieldKeys()
    {
        var storage = new FakeStorageProvider("shared");
        Assert.Throws<InvalidOperationException>(() => new InstanceProviderRegistry([storage, new FakeStorageProvider("shared")]));
        var inconsistent = new FakeInstanceProvider(storage.Descriptor with
        {
            Id = "other",
            Type = storage.Descriptor.Type with { Name = "Different type" },
        });
        Assert.Throws<InvalidOperationException>(() => new InstanceProviderRegistry([storage, inconsistent]));
        foreach (IReadOnlyList<InstanceConfigurationField> fields in new IReadOnlyList<InstanceConfigurationField>[]
        {
            [new("same", "First"), new("same", "Second")],
            [new("bad key", "Field")], [new("", "Field")],
            [new("token", "Field", MaxLength: -1)],
            [new("token", "Field", Kind: (InstanceFieldKind)99)],
        })
        {
            var provider = new FakeInstanceProvider(storage.Descriptor with { Fields = fields });
            Assert.Throws<InvalidOperationException>(() => new InstanceProviderRegistry([provider]));
        }
    }

    /// <summary>Docker 使用自己的字段并重启恢复；同名存储插件不能读取其密文配置。</summary>
    [Fact]
    public async Task Configuration_PersistsNonStorageParametersAndRejectsWrongTypeConnector()
    {
        using var environment = new LocalStorageTestEnvironment();
        string instanceId;
        FakeInstanceProvider docker = CreateDockerProvider("shared");
        var registration = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(environment.CreateConfiguration());
        registration.AddSingleton<IInstanceProvider>(docker);
        using (var services = registration.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }))
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var configuration = services.GetRequiredService<IInstanceConfigurationService>();
            InstanceSummary instance = await configuration.CreateAsync(new InstanceConfigurationDraft
            {
                Name = "Docker host",
                Connection = new InstanceConnectionOptions
                {
                    TypeId = "docker",
                    ProviderId = docker.Descriptor.Id,
                    Parameters = new Dictionary<string, string>
                    {
                        ["engineEndpoint"] = "https://docker.example.test",
                        ["engineToken"] = "future-private-token",
                    },
                },
            }, TestContext.Current.CancellationToken);
            instanceId = instance.Id;
            Assert.Equal("docker", instance.TypeId);
            Assert.Equal("shared", instance.ProviderId);
            Assert.True(instance.HasConfiguration);
            Assert.Null(docker.ReceivedOptions);
            byte[] cipher = (byte[])services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar(
                "SELECT protected_parameters FROM instance_configuration WHERE id = @id;", new { id = instanceId });
            Assert.DoesNotContain("future-private-token", System.Text.Encoding.UTF8.GetString(cipher));
        }

        var restartRegistration = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(environment.CreateConfiguration());
        restartRegistration.AddSingleton<IInstanceProvider>(CreateDockerProvider("shared"));
        using (var restarted = restartRegistration.BuildServiceProvider())
        {
            restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            InstanceConnectionOptions? recovered = await restarted.GetRequiredService<IInstanceConfigurationService>()
                .GetConnectionAsync(instanceId, TestContext.Current.CancellationToken);
            Assert.NotNull(recovered);
            Assert.Equal("docker", recovered.TypeId);
            Assert.Equal("shared", recovered.ProviderId);
            Assert.Equal("future-private-token", recovered.Parameters["engineToken"]);
            Assert.Equal("https://docker.example.test", recovered.Parameters["engineEndpoint"]);
            Assert.DoesNotContain("accessKeyId", recovered.Parameters.Keys);
        }

        var wrongType = new FakeStorageProvider("shared");
        var missingRegistration = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(environment.CreateConfiguration());
        missingRegistration.RemoveAll<IInstanceProvider>();
        missingRegistration.AddSingleton<IInstanceProvider>(wrongType);
        using var missing = missingRegistration.BuildServiceProvider();
        missing.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var unavailable = missing.GetRequiredService<IInstanceConfigurationService>();
        Assert.Equal("docker", Assert.Single(unavailable.GetInstances()).TypeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unavailable.GetConnectionAsync(instanceId,
            TestContext.Current.CancellationToken));
        Assert.Null(wrongType.ReceivedOptions);
    }

    /// <summary>声明必填字段的非存储插件也在本地保存前接受同一规则校验。</summary>
    [Fact]
    public async Task Configuration_ValidatesDeclaredFieldsBeforeSaving()
    {
        using var environment = new LocalStorageTestEnvironment();
        var registration = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(environment.CreateConfiguration());
        registration.AddSingleton<IInstanceProvider>(CreateDockerProvider("docker-engine"));
        using var services = registration.BuildServiceProvider();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configuration = services.GetRequiredService<IInstanceConfigurationService>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.CreateAsync(new InstanceConfigurationDraft
        {
            Name = "Incomplete Docker",
            Connection = new InstanceConnectionOptions
            {
                TypeId = "docker",
                ProviderId = "docker-engine",
                Parameters = new Dictionary<string, string> { ["accessKeyId"] = "storage-parameters" },
            },
        }, TestContext.Current.CancellationToken));

        Assert.Empty(configuration.GetInstances());
    }

    /// <summary>非存储替身通过通用接口注册，字段与存储凭据完全独立。</summary>
    private static FakeInstanceProvider CreateDockerProvider(string id) => new(new(id, "Docker Engine", "Container engine", "container",
        new("docker", "Docker", "连接容器服务", "container"),
        [new("engineEndpoint", "Engine endpoint", MaxLength: 2048),
         new("engineToken", "Engine token", InstanceFieldKind.Secret, MaxLength: 256, IsSensitive: true)]));
}
