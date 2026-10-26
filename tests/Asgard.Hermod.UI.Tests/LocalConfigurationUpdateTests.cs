using Asgard.Hermod.Abstractions.Localization;
using Asgard.Hermod.Providers.VolcengineTos;

namespace Asgard.Hermod.UI.Tests;

/// <summary>编辑真实持久化实例时保留身份与关联，只替换已加密配置和名称。</summary>
public sealed class LocalConfigurationUpdateTests
{
    /// <summary>编辑结果、重启恢复与 SQLite 实际记录一致，旧标签不会因草稿缺少关联而丢失。</summary>
    [Fact]
    public async Task UpdateAsync_PreservesIdentityCreationTimeAndTagsAcrossRestart()
    {
        using var environment = new LocalStorageTestEnvironment();
        string instanceId;
        DateTime creationTime;
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var configuration = services.GetRequiredService<IInstanceConfigurationService>();
            InstanceSummary original = await configuration.CreateAsync(CreateDraft("Original", "original-private-secret",
                ["builtin.environment.staging"]), TestContext.Current.CancellationToken);
            instanceId = original.Id;
            var repository = services.GetRequiredService<ILocalConfigurationRepository>();
            StoredInstanceConfiguration stored = repository.GetInstance(instanceId, TestContext.Current.CancellationToken)!;
            creationTime = stored.CreatedAt;

            InstanceSummary updated = await configuration.UpdateAsync(instanceId, CreateDraft("Renamed", "updated-private-secret", []),
                TestContext.Current.CancellationToken);

            Assert.Equal(instanceId, updated.Id);
            Assert.Equal("Renamed", updated.Name);
            Assert.Equal(["Environment/Staging"], updated.Tags);
            Assert.Single(configuration.GetInstances());
            StoredInstanceConfiguration changed = repository.GetInstance(instanceId, TestContext.Current.CancellationToken)!;
            Assert.Equal(creationTime, changed.CreatedAt);
            Assert.Equal(stored.TagIds, changed.TagIds);
            Assert.NotEqual(stored.ProtectedParameters, changed.ProtectedParameters);
        }
        using var restarted = environment.CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restarted.GetRequiredService<IInstanceConfigurationService>();
        Assert.Equal("Renamed", Assert.Single(restored.GetInstances()).Name);
        InstanceConnectionOptions? connection = await restored.GetConnectionAsync(instanceId, TestContext.Current.CancellationToken);
        Assert.NotNull(connection);
        Assert.Equal("updated-private-secret", connection.Parameters["accessKeySecret"]);
        Assert.Equal(creationTime, restarted.GetRequiredService<ILocalConfigurationRepository>()
            .GetInstance(instanceId, TestContext.Current.CancellationToken)!.CreatedAt);
    }

    /// <summary>缺失实例、无效连接字段与调用方取消均不能新增或部分改写旧配置。</summary>
    [Fact]
    public async Task UpdateAsync_RejectsInvalidOrCancelledChangesWithoutWrites()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configuration = services.GetRequiredService<IInstanceConfigurationService>();
        InstanceSummary original = await configuration.CreateAsync(CreateDraft("Original", "original-private-secret", []),
            TestContext.Current.CancellationToken);
        var repository = services.GetRequiredService<ILocalConfigurationRepository>();
        StoredInstanceConfiguration stored = repository.GetInstance(original.Id, TestContext.Current.CancellationToken)!;

        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.UpdateAsync("missing-instance",
            CreateDraft("Missing", "new-secret", []), TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.UpdateAsync(original.Id,
            CreateDraft("Invalid", "", []), TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => configuration.UpdateAsync(original.Id,
            CreateDraft("Cancelled", "new-secret", []), cancellation.Token));

        Assert.Equal("Original", Assert.Single(configuration.GetInstances()).Name);
        Assert.Equal(stored.ProtectedParameters, repository.GetInstance(original.Id, TestContext.Current.CancellationToken)!.ProtectedParameters);
    }

    /// <summary>编辑可以切换提供商，但持久化边界仍拒绝更改实例类型。</summary>
    [Fact]
    public async Task UpdateAsync_RejectsChangingInstanceType()
    {
        using var environment = new LocalStorageTestEnvironment();
        var registration = new ServiceCollection().AddHermodCore().AddWindowsSecretProtection().AddObjectStorage().AddAliyunOss().AddLocalStorage(environment.CreateConfiguration());
        registration.AddSingleton<IInstanceProvider>(new FakeInstanceProvider(new("docker-provider", "Docker", "Docker", "container",
            new("docker", "Docker", "连接容器服务", "container"), [])));
        using var services = registration.BuildServiceProvider();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configuration = services.GetRequiredService<IInstanceConfigurationService>();
        InstanceSummary original = await configuration.CreateAsync(CreateDraft("Original", "secret", []), TestContext.Current.CancellationToken);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.UpdateAsync(original.Id,
            new InstanceConfigurationDraft
            {
                Name = "Changed type",
                Connection = new InstanceConnectionOptions { TypeId = "docker", ProviderId = "docker-provider", Parameters = new Dictionary<string, string>() },
            }, TestContext.Current.CancellationToken));
        Assert.Equal(Text.Get("EditingCannotChangeTheInstanceTypeCreateAnInstanceOfTheRequiredTypeInstead"), Text.Error(error));
        InstanceSummary unchanged = Assert.Single(configuration.GetInstances());
        Assert.Equal(original.TypeId, unchanged.TypeId);
        Assert.Equal(original.ProviderId, unchanged.ProviderId);
        Assert.Equal("Original", unchanged.Name);
    }

    /// <summary>真实存储提供商切换保留原记录与标签，重启后完整 TOS 地址和旧区域值均保持不变。</summary>
    [Theory]
    [InlineData("cn-beijing")]
    [InlineData("https://tos-cn-shanghai.volces.com")]
    public async Task UpdateAsync_ChangesStorageProviderAndPreservesIdentityAcrossRestart(string tosEndpoint)
    {
        using var environment = new LocalStorageTestEnvironment();
        ServiceProvider CreateServices() => new ServiceCollection().AddHermodCore().AddWindowsSecretProtection()
            .AddObjectStorage().AddAliyunOss().AddVolcengineTos().AddLocalStorage(environment.CreateConfiguration()).BuildServiceProvider();
        StoredInstanceConfiguration original;
        using (var services = CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var configurations = services.GetRequiredService<IInstanceConfigurationService>();
            var summary = await configurations.CreateAsync(CreateDraft("Original", "old-provider-secret",
                ["builtin.environment.staging"]), TestContext.Current.CancellationToken);
            var repository = services.GetRequiredService<ILocalConfigurationRepository>();
            original = repository.GetInstance(summary.Id, TestContext.Current.CancellationToken)!;
            var connection = new InstanceConnectionOptions
            {
                TypeId = "storage",
                ProviderId = "volcengine-tos",
                Parameters = new Dictionary<string, string>
                {
                    ["accessKeyId"] = "VolcengineTestKey",
                    ["accessKeySecret"] = "new-provider-secret",
                    ["region"] = tosEndpoint,
                },
            };
            var updated = await configurations.UpdateAsync(summary.Id, new() { Name = "Corrected provider", Connection = connection },
                TestContext.Current.CancellationToken);
            Assert.Equal(summary.Id, updated.Id);
            Assert.Equal("storage", updated.TypeId);
            Assert.Equal("volcengine-tos", updated.ProviderId);
            Assert.Equal(summary.Tags, updated.Tags);
            var stored = repository.GetInstance(summary.Id, TestContext.Current.CancellationToken)!;
            Assert.Equal(original.CreatedAt, stored.CreatedAt);
            Assert.Equal(original.TagIds, stored.TagIds);
            Assert.NotEqual(original.ProtectedParameters, stored.ProtectedParameters);

            await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.UpdateAsync(summary.Id, new()
            {
                Name = "Invalid provider configuration",
                Connection = new() { TypeId = connection.TypeId, ProviderId = connection.ProviderId, Parameters = new Dictionary<string, string>() },
            }, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<InvalidOperationException>(() => configurations.UpdateAsync(summary.Id, new()
            {
                Name = "Unknown provider",
                Connection = new() { TypeId = connection.TypeId, ProviderId = "unregistered-provider", Parameters = connection.Parameters },
            }, TestContext.Current.CancellationToken));
            Assert.Equal(stored.ProtectedParameters, repository.GetInstance(summary.Id, TestContext.Current.CancellationToken)!.ProtectedParameters);
        }
        using var restarted = CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restarted.GetRequiredService<IInstanceConfigurationService>();
        var instance = Assert.Single(restored.GetInstances());
        Assert.Equal(original.Id, instance.Id);
        Assert.Equal("Corrected provider", instance.Name);
        Assert.Equal("volcengine-tos", instance.ProviderId);
        Assert.Equal(["Environment/Staging"], instance.Tags);
        var saved = await restored.GetConnectionAsync(instance.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("volcengine-tos", saved.ProviderId);
        Assert.Equal("new-provider-secret", saved.Parameters["accessKeySecret"]);
        bool fullEndpoint = tosEndpoint.StartsWith("https://", StringComparison.Ordinal);
        Assert.Equal(fullEndpoint ? "endpoint" : "region", saved.Parameters["endpointMode"]);
        Assert.Equal(tosEndpoint, saved.Parameters[fullEndpoint ? "endpoint" : "region"]);
        Assert.False(saved.Parameters.ContainsKey(fullEndpoint ? "region" : "endpoint"));
    }

    /// <summary>采用虚构有效字段，所有测试都不访问云服务。</summary>
    private static InstanceConfigurationDraft CreateDraft(string name, string secret, IReadOnlyList<string> tags) => new()
    {
        Name = name,
        TagIds = tags,
        Connection = new InstanceConnectionOptions
        {
            TypeId = AliyunOssStorageProvider.TypeId,
            ProviderId = AliyunOssStorageProvider.ProviderId,
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "TestAccessKey", ["accessKeySecret"] = secret },
        },
    };
}
