using System.Text;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用实际 SQLite 与 Windows DPAPI 验证跨设备快照和会话持久化。</summary>
public sealed class WorkspaceSnapshotStorageTests
{
    /// <summary>完整快照重新保护凭据，标签层级与实例稳定身份在重启后保留。</summary>
    [Fact]
    public async Task Snapshot_RestoresCredentialsAndTagsAcrossDatabases()
    {
        using var sourceEnvironment = new LocalStorageTestEnvironment(); using var targetEnvironment = new LocalStorageTestEnvironment();
        WorkspaceSnapshot snapshot;
        using (var source = sourceEnvironment.CreateServices())
        {
            source.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var tags = source.GetRequiredService<ITagManagementService>();
            var root = await tags.SaveAsync(null, new TagEditDraft("Project", null, "#123456"), TestContext.Current.CancellationToken);
            var child = await tags.SaveAsync(null, new TagEditDraft("Production", root.Id, "#123456"), TestContext.Current.CancellationToken);
            await source.GetRequiredService<IInstanceConfigurationService>().CreateAsync(new()
            {
                Name = "Cloud instance",
                TagIds = [child.Id, "builtin.environment.production"],
                Connection = new()
                {
                    TypeId = AliyunOssStorageProvider.TypeId,
                    ProviderId = AliyunOssStorageProvider.ProviderId,
                    Parameters = new Dictionary<string, string> { ["accessKeyId"] = "PrivateKeyId123", ["accessKeySecret"] = "credential-value-123" }
                },
            }, TestContext.Current.CancellationToken);
            snapshot = await source.GetRequiredService<ILocalWorkspaceStore>().ExportAsync(TestContext.Current.CancellationToken);
        }
        using (var target = targetEnvironment.CreateServices())
        {
            target.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            await target.GetRequiredService<ILocalWorkspaceStore>().RestoreAsync(snapshot, TestContext.Current.CancellationToken);
            byte[] protectedData = (byte[])target.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT protected_parameters FROM instance_configuration;");
            Assert.DoesNotContain("credential-value-123", Encoding.UTF8.GetString(protectedData));
        }
        using var restarted = targetEnvironment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = await restarted.GetRequiredService<ILocalWorkspaceStore>().ExportAsync(TestContext.Current.CancellationToken);
        Assert.Equal(snapshot.Instances[0].Id, restored.Instances[0].Id);
        Assert.Equal("credential-value-123", restored.Instances[0].Parameters["accessKeySecret"]);
        Assert.Equal(snapshot.Tags.OrderBy(tag => tag.Id), restored.Tags.OrderBy(tag => tag.Id));
    }
    /// <summary>损坏层级不能删除已有工作区；用户级设置也不属于下载覆盖范围。</summary>
    [Fact]
    public async Task InvalidSnapshot_DoesNotReplaceDataOrSettings()
    {
        using var environment = new LocalStorageTestEnvironment(); using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        services.GetRequiredService<ILanguagePreferenceStore>().Save("en");
        var tag = await services.GetRequiredService<ITagManagementService>().SaveAsync(null, new TagEditDraft("Keep", null, "#123456"), TestContext.Current.CancellationToken);
        var store = services.GetRequiredService<ILocalWorkspaceStore>(); var original = await store.ExportAsync(TestContext.Current.CancellationToken);
        var broken = original with { Tags = original.Tags.Select(value => value.Id == tag.Id ? value with { ParentId = tag.Id } : value).ToArray() };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RestoreAsync(broken, TestContext.Current.CancellationToken));
        Assert.Equal(original.Tags, (await store.ExportAsync(TestContext.Current.CancellationToken)).Tags);
        Assert.Equal("en", services.GetRequiredService<ILanguagePreferenceStore>().Load());
    }
    /// <summary>会话落盘只有 DPAPI 密文，重启后恢复；版本按用户隔离。</summary>
    [Fact]
    public void SessionStore_ProtectsTokenAndPersistsSeparateRevisions()
    {
        using var environment = new LocalStorageTestEnvironment();
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var store = services.GetRequiredService<IAccountSessionStore>(); store.SaveSession("private-access-token");
            store.SaveRevision("owner-a", "revision-a");
            string value = (string)services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'identity.session';");
            Assert.DoesNotContain("private-access-token", Encoding.UTF8.GetString(Convert.FromBase64String(value)));
        }
        using var restarted = environment.CreateServices(); restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var restored = restarted.GetRequiredService<IAccountSessionStore>();
        Assert.Equal("private-access-token", restored.LoadSession()); Assert.Equal("revision-a", restored.LoadRevision("owner-a"));
        Assert.Equal("0", restored.LoadRevision("owner-b")); restored.SaveSession(null); Assert.Null(restored.LoadSession());
    }
}
