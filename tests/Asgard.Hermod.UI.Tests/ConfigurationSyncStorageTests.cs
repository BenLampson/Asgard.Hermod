using System.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 SQLite 与 DPAPI 验证密码、基准及原子账户切换。</summary>
public sealed class ConfigurationSyncStorageTests
{
    /// <summary>密码和凭据不能作为明文落盘，重建服务恢复原状态。</summary>
    [Fact]
    public async Task State_ProtectsPasswordAndPersistsPerAccount()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var snapshot = await services.GetRequiredService<ILocalWorkspaceStore>().ExportAsync(TestContext.Current.CancellationToken);
            var store = services.GetRequiredService<IConfigurationSyncStateStore>();
            store.Save("account-a", new("saved-password-123", snapshot, snapshot, "revision-a", DateTime.UnixEpoch));
            store.ActiveOwner = "account-a";
            string saved = (string)services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'sync.state.account-a';");
            Assert.DoesNotContain("saved-password-123", saved);
        }
        Assert.DoesNotContain("saved-password-123", Encoding.UTF8.GetString(File.ReadAllBytes(environment.CreateConfiguration().DatabasePath)));
        using var restarted = environment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var stateStore = restarted.GetRequiredService<IConfigurationSyncStateStore>();
        Assert.Equal("account-a", stateStore.ActiveOwner); Assert.Equal("saved-password-123", stateStore.Load("account-a").Password);
        Assert.Equal("revision-a", stateStore.Load("account-a").Revision); Assert.Null(stateStore.Load("account-b").Password);
        stateStore.Save("account-a", stateStore.Load("account-a") with { Password = null }); Assert.Null(stateStore.Load("account-a").Password);
        Assert.NotNull(stateStore.Load("account-a").Baseline);
    }
    /// <summary>网络期间发生本机编辑时拒绝替换，归属标识也不能提前切换。</summary>
    [Fact]
    public async Task ConditionalRestore_ProtectsEditsAndCommitsOwnershipAtomically()
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var store = services.GetRequiredService<ILocalWorkspaceStore>(); var states = services.GetRequiredService<IConfigurationSyncStateStore>();
        var original = await store.ExportAsync(TestContext.Current.CancellationToken);
        await services.GetRequiredService<ITagManagementService>().SaveAsync(null, new("Keep local edit", null, "#123456"), TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreIfUnchangedAsync(original, original, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SwitchOwnerAsync("other", original, original, TestContext.Current.CancellationToken));
        Assert.Empty(states.ActiveOwner);
        var changed = await store.ExportAsync(TestContext.Current.CancellationToken); Assert.Contains(changed.Tags, tag => tag.Name == "Keep local edit");
        await store.SwitchOwnerAsync("other", original, changed, TestContext.Current.CancellationToken);
        Assert.Equal("other", states.ActiveOwner); Assert.True(WorkspaceContent.Equal(original, await store.ExportAsync(TestContext.Current.CancellationToken)));
    }
}
