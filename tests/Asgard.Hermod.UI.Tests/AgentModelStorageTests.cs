using System.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>真实 SQLite 和 DPAPI 验证模型密文、事务快照和损坏保护，不创建窗口。</summary>
public sealed class AgentModelStorageTests
{
    private static readonly AgentModelProfile Profile = new("11111111111111111111111111111111", "Work model", "https://models.example/v1", "test-model", "private-model-credential", DateTimeOffset.UnixEpoch);

    /// <summary>重启后保留配置；数据库只有密文，成功迁移才删除旧设置。</summary>
    [Fact]
    public void Save_ProtectsAllFieldsAndPersistsAcrossRestart()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IAgentModelStore>(); Assert.Null(store.Load());
            var db = services.GetRequiredService<IFreeSql>();
            db.Ado.ExecuteNonQuery("INSERT INTO app_setting VALUES ('agent.model', '{}', '2026-01-01');");
            int changed = 0; store.Changed += (_, _) => { changed++; Assert.Single(store.Load()!.Profiles); };
            store.Save(new([Profile], Profile.Id)); Assert.Equal(1, changed);
            string ciphertext = (string)db.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'agent.models';");
            string bytes = Encoding.UTF8.GetString(Convert.FromBase64String(ciphertext));
            foreach (string secret in new[] { Profile.Name, Profile.Endpoint, Profile.Model, Profile.ApiKey }) Assert.DoesNotContain(secret, bytes);
            Assert.Equal(0L, db.Ado.ExecuteScalar("SELECT COUNT(*) FROM app_setting WHERE id = 'agent.model';"));
        }
        using var restarted = environment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.Equal(Profile, Assert.Single(restarted.GetRequiredService<IAgentModelStore>().Load()!.Profiles));
    }

    /// <summary>损坏密文不能被解释为空集合，错误不显示密文或原始解密信息。</summary>
    [Fact]
    public async Task CorruptedCiphertext_RejectsLoadAndExportWithoutReplacement()
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize(); var store = services.GetRequiredService<IAgentModelStore>();
        store.Save(new([Profile], Profile.Id)); var db = services.GetRequiredService<IFreeSql>();
        db.Ado.ExecuteNonQuery("UPDATE app_setting SET value = 'broken-private-ciphertext' WHERE id = 'agent.models';");
        var error = Assert.Throws<InvalidOperationException>(() => store.Load());
        Assert.Equal("AgentModelUnlockFailed", error.Data["Hermod.ResourceKey"]);
        Assert.DoesNotContain("broken-private", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => services.GetRequiredService<ILocalWorkspaceStore>().ExportAsync(TestContext.Current.CancellationToken));
        Assert.Equal("broken-private-ciphertext", db.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'agent.models';"));
    }

    /// <summary>模型参与比较交换；过期恢复不能覆盖用户刚改的模型或其他工作区内容。</summary>
    [Fact]
    public async Task Snapshot_ModelsParticipateInAtomicComparisonAndOwnerSwitch()
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var models = services.GetRequiredService<IAgentModelStore>(); var workspace = services.GetRequiredService<ILocalWorkspaceStore>();
        models.Save(new([Profile], Profile.Id)); var before = await workspace.ExportAsync(TestContext.Current.CancellationToken);
        var changed = Profile with { ApiKey = "new-private-key" }; models.Save(new([changed], Profile.Id));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.RestoreIfUnchangedAsync(before with { AgentModels = AgentModelSet.Empty }, before, TestContext.Current.CancellationToken));
        Assert.Equal(changed, Assert.Single(models.Load()!.Profiles));
        var current = await workspace.ExportAsync(TestContext.Current.CancellationToken);
        await workspace.SwitchOwnerAsync("owner-b", current with { AgentModels = AgentModelSet.Empty }, current, TestContext.Current.CancellationToken);
        Assert.Empty(models.Load()!.Profiles); Assert.Equal("owner-b", services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'sync.activeOwner';"));
    }
}
