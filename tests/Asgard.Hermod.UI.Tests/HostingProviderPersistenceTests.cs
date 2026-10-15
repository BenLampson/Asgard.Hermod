using Asgard.Hermod.Abstractions.Security;

namespace Asgard.Hermod.UI.Tests;

/// <summary>托管品牌只更新展示列，覆盖三类实例的真实 SQLite、密文和同步快照。</summary>
public sealed class HostingProviderPersistenceTests
{
    /// <summary>品牌修改无需解密，连接器、凭据、标签和创建时间均不受影响。</summary>
    [Theory]
    [InlineData("ssh")]
    [InlineData("mysql")]
    [InlineData("redis")]
    public async Task BrandEdit_PreservesConnectionAndSurvivesRestartAndRestore(string type)
    {
        using var environment = new LocalStorageTestEnvironment();
        ServiceProvider CreateServices() => Services(environment, type);
        string id;
        using (var services = CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IInstanceConfigurationService>();
            var original = await store.CreateAsync(Draft(type), TestContext.Current.CancellationToken);
            id = original.Id;
            var repository = services.GetRequiredService<ILocalConfigurationRepository>();
            var before = repository.GetInstance(id, TestContext.Current.CancellationToken)!;
            var metadata = new LocalInstanceConfigurationService(repository, services.GetRequiredService<IInstanceProviderRegistry>(), new RejectSecretAccess());

            var edited = await metadata.UpdateMetadataAsync(id, new() { HostingProviderId = "volcengine" }, TestContext.Current.CancellationToken);

            var after = repository.GetInstance(id, TestContext.Current.CancellationToken)!;
            Assert.Equal(before.ProtectedParameters, after.ProtectedParameters);
            Assert.Equal(before.CreatedAt, after.CreatedAt);
            Assert.Equal(before.ProviderId, after.ProviderId);
            Assert.Equal(before.TypeId, after.TypeId);
            Assert.Equal(before.TagIds, after.TagIds);
            Assert.Equal("volcengine", edited.HostingProviderId);
            var row = new MainInstanceRow(new(edited), null);
            Assert.False(row.IsSelfHosted);
            Assert.Equal(InstanceHostingProviders.Get("volcengine").Name, row.ProviderName);
            Assert.Equal("provider-volcengine-tos", row.ProviderIcon);
            var connection = (await store.GetConnectionAsync(id, TestContext.Current.CancellationToken))!;
            Assert.Equal("transport", connection.ProviderId);
            Assert.Equal("unchanged-secret", connection.Parameters["password"]);

            // 旧连接编辑调用未指定品牌时必须保留刚刚保存的品牌。
            await store.UpdateAsync(id, new() { Name = original.Name, Connection = connection }, TestContext.Current.CancellationToken);
            Assert.Equal("volcengine", Assert.Single(store.GetInstances()).HostingProviderId);
            var workspace = services.GetRequiredService<ILocalWorkspaceStore>();
            var exported = await workspace.ExportAsync(TestContext.Current.CancellationToken);
            Assert.Equal("volcengine", Assert.Single(exported.Instances).HostingProviderId);
            await metadata.UpdateMetadataAsync(id, new() { HostingProviderId = "" }, TestContext.Current.CancellationToken);
            Assert.True(new MainInstanceRow(new(Assert.Single(store.GetInstances())), null).IsSelfHosted);
            await workspace.RestoreAsync(exported, TestContext.Current.CancellationToken);
        }
        using var reopened = CreateServices();
        reopened.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = reopened.GetRequiredService<IInstanceConfigurationService>();
        Assert.Equal(id, Assert.Single(restored.GetInstances()).Id);
        Assert.Equal("volcengine", Assert.Single(restored.GetInstances()).HostingProviderId);
        Assert.Equal("unchanged-secret", (await restored.GetConnectionAsync(id, TestContext.Current.CancellationToken))!.Parameters["password"]);
    }

    /// <summary>品牌或标签无效时，名称和品牌在同一事务回滚。</summary>
    [Fact]
    public async Task InvalidMetadata_RollsBackNameAndBrandTogether()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = Services(environment, "ssh");
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<IInstanceConfigurationService>();
        var original = await store.CreateAsync(Draft("ssh"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateMetadataAsync(original.Id,
            new() { Name = "Must not persist", HostingProviderId = "volcengine", TagIds = ["missing"] }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.UpdateMetadataAsync(original.Id,
            new() { HostingProviderId = "translated-or-unregistered-brand" }, TestContext.Current.CancellationToken));
        var unchanged = Assert.Single(store.GetInstances());
        Assert.Equal(original.Name, unchanged.Name);
        Assert.Equal("aliyun", unchanged.HostingProviderId);
    }

    /// <summary>版本三升级只新增默认展示列，不重写密文和连接器。</summary>
    [Fact]
    public async Task VersionThreeMigration_PreservesExistingConnection()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = Services(environment, "ssh");
        var initializer = services.GetRequiredService<LocalDatabaseInitializer>();
        initializer.Initialize();
        var store = services.GetRequiredService<IInstanceConfigurationService>();
        var original = await store.CreateAsync(Draft("ssh"), TestContext.Current.CancellationToken);
        var repository = services.GetRequiredService<ILocalConfigurationRepository>();
        var before = repository.GetInstance(original.Id, TestContext.Current.CancellationToken)!;
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("ALTER TABLE instance_configuration DROP COLUMN hosting_provider_id; PRAGMA user_version = 3;");
        initializer.Initialize();
        initializer.Initialize();
        var migrated = repository.GetInstance(original.Id, TestContext.Current.CancellationToken)!;
        Assert.Empty(migrated.HostingProviderId);
        Assert.Equal(before.ProtectedParameters, migrated.ProtectedParameters);
        Assert.Equal(before.ProviderId, migrated.ProviderId);
        Assert.Equal(before.CreatedAt, migrated.CreatedAt);
        Assert.Equal(before.TagIds, migrated.TagIds);
        Assert.Equal(4, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
    }

    internal static ServiceProvider Services(LocalStorageTestEnvironment environment, string type) =>
        new ServiceCollection().AddSingleton<IInstanceProvider>(new FakeInstanceProvider(new("transport", "Transport", "Transport", "terminal",
            new(type, type, "", "terminal"), []))).AddHermodCore().AddWindowsSecretProtection()
            .AddLocalStorage(environment.CreateConfiguration()).BuildServiceProvider();

    internal static InstanceConfigurationDraft Draft(string type) => new()
    {
        Name = "Hosting brand test",
        HostingProviderId = "aliyun",
        TagIds = ["builtin.environment.production"],
        Connection = new()
        {
            TypeId = type,
            ProviderId = "transport",
            Parameters = new Dictionary<string, string> { ["host"] = "server.example", ["password"] = "unchanged-secret" }
        }
    };

    private sealed class RejectSecretAccess : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext) => throw new InvalidOperationException("Metadata must not encrypt credentials.");
        public byte[] Unprotect(ReadOnlySpan<byte> ciphertext) => throw new InvalidOperationException("Metadata must not decrypt credentials.");
    }
}
