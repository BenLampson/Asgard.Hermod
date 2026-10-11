using System.Text;
using Asgard.Hermod.Infrastructure.Security;
using Asgard.Hermod.Infrastructure.Storage;

namespace Asgard.Hermod.UI.Tests;

/// <summary>验证真实配置从版本一升级、加密保存、重启恢复和边界隔离。</summary>
public sealed class LocalConfigurationTests
{
    /// <summary>升级已存在的设置数据库时保留设置并建立固定环境标签。</summary>
    [Fact]
    public async Task Initialize_MigratesVersionOneWithoutLosingSettings()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("""
            CREATE TABLE app_setting (id TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL, update_time TEXT NOT NULL);
            INSERT INTO app_setting VALUES ('theme', 'saved-dark', '2026-10-12');
            PRAGMA user_version = 1;
            """);

        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        Assert.Equal("saved-dark", database.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'theme';"));
        Assert.Equal(LocalDatabaseInitializer.CurrentSchemaVersion, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        var tags = await services.GetRequiredService<ITagManagementService>().GetTagsAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, tags.Count);
        Assert.Contains(tags, tag => tag.Id == "builtin.environment.production" && tag.Path == "Environment/Production" && tag.Color == "#EF4444");
        Assert.Contains(tags, tag => tag.Id == "builtin.environment.staging" && tag.Color == "#F59E0B");
        Assert.Contains(tags, tag => tag.Id == "builtin.environment.development" && tag.Color == "#22C55E");
    }

    /// <summary>版本二迁移中任何冲突都回滚全部新表，保持旧版本与原设置。</summary>
    [Fact]
    public void Initialize_RollsBackVersionTwoMigrationOnConflict()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("""
            CREATE TABLE app_setting (id TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL, update_time TEXT NOT NULL);
            INSERT INTO app_setting VALUES ('sentinel', 'saved', '2026-10-12');
            CREATE TABLE managed_tag (sentinel TEXT);
            PRAGMA user_version = 1;
            """);

        Assert.Throws<SQLiteException>(services.GetRequiredService<LocalDatabaseInitializer>().Initialize);

        Assert.Equal(1, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal("saved", database.Ado.ExecuteScalar("SELECT value FROM app_setting;"));
        Assert.Equal(0, Convert.ToInt32(database.Ado.ExecuteScalar(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'instance_configuration';")));
    }

    /// <summary>参数含 AK 也只保存 DPAPI 密文；重启后显式连接读取可以恢复完整第三方字段。</summary>
    [Fact]
    public async Task CreateAsync_EncryptsAllParametersAndPersistsAcrossRestart()
    {
        using var environment = new LocalStorageTestEnvironment();
        const string accessKeyId = "LTAITestPrivateAccessKey123";
        const string accessKeySecret = "private-secret-value-123";
        const string customValue = "future-provider-private-parameter";
        var options = new InstanceConnectionOptions
        {
            TypeId = AliyunOssStorageProvider.TypeId,
            ProviderId = AliyunOssStorageProvider.ProviderId,
            Parameters = new Dictionary<string, string>
            {
                ["accessKeyId"] = accessKeyId,
                ["accessKeySecret"] = accessKeySecret,
                ["futureParameter"] = customValue,
            },
        };
        var draft = new InstanceConfigurationDraft
        {
            Name = "My OSS",
            Connection = options,
            TagIds = ["builtin.environment.production"],
        };
        string instanceId;
        using (var services = environment.CreateServices())
        {
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
            var source = services.GetRequiredService<IWorkspaceDataSource>();
            Assert.Empty(source.GetInstances());
            InstanceSummary summary = await services.GetRequiredService<IInstanceConfigurationService>()
                .CreateAsync(draft, TestContext.Current.CancellationToken);
            instanceId = summary.Id;
            Assert.Equal(["Environment/Production"], summary.Tags);
            Assert.False(summary.IsLocal);
            Assert.True(summary.HasConfiguration);
            Assert.Equal(AliyunOssStorageProvider.TypeId, summary.TypeId);
            Assert.Equal(AliyunOssStorageProvider.ProviderId, summary.ProviderId);
            Assert.Equal("阿里云 OSS", summary.Endpoint);
            Assert.Equal("My OSS", summary.DisplayName);
            Assert.Null(source.GetServiceSnapshot(instanceId));
            byte[] ciphertext = (byte[])services.GetRequiredService<IFreeSql>().Ado.ExecuteScalar(
                "SELECT protected_parameters FROM instance_configuration WHERE id = @id;", new { id = instanceId });
            string raw = Encoding.UTF8.GetString(ciphertext);
            foreach (string secret in new[] { accessKeyId, accessKeySecret, customValue })
            {
                Assert.DoesNotContain(secret, raw);
                Assert.DoesNotContain(secret, draft.ToString());
                Assert.DoesNotContain(secret, options.ToString());
                Assert.DoesNotContain(secret, summary.ToString());
                Assert.DoesNotContain(secret, services.GetRequiredService<ILocalConfigurationRepository>()
                    .GetSnapshot(TestContext.Current.CancellationToken).ToString());
            }
            byte[] decoded = services.GetRequiredService<ISecretProtector>().Unprotect(ciphertext);
            try { Assert.Contains(accessKeyId, Encoding.UTF8.GetString(decoded)); }
            finally { System.Security.Cryptography.CryptographicOperations.ZeroMemory(decoded); }
        }

        // 容器关闭后检查数据库与旁路文件，防止查询列之外意外留存明文。
        foreach (string file in Directory.EnumerateFiles(environment.DirectoryPath, "*", SearchOption.AllDirectories))
        {
            string contents = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            Assert.DoesNotContain(accessKeyId, contents);
            Assert.DoesNotContain(accessKeySecret, contents);
            Assert.DoesNotContain(customValue, contents);
        }
        using var restarted = environment.CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configuration = restarted.GetRequiredService<IInstanceConfigurationService>();
        InstanceSummary restored = Assert.Single(configuration.GetInstances());
        Assert.Equal(instanceId, restored.Id);
        Assert.Equal("My OSS", restored.Name);
        InstanceConnectionOptions? recovered = await configuration.GetConnectionAsync(instanceId, TestContext.Current.CancellationToken);
        Assert.NotNull(recovered);
        Assert.Equal(AliyunOssStorageProvider.TypeId, recovered.TypeId);
        Assert.Equal(AliyunOssStorageProvider.ProviderId, recovered.ProviderId);
        Assert.Equal(accessKeyId, recovered.Parameters["accessKeyId"]);
        Assert.Equal(accessKeySecret, recovered.Parameters["accessKeySecret"]);
        Assert.Equal(customValue, recovered.Parameters["futureParameter"]);
    }

    /// <summary>账户、未知连接器和已取消操作都不能悄悄产生本地实例。</summary>
    [Fact]
    public async Task CreateAsync_RejectsUnsupportedScopeProviderAndCancellationWithoutWrites()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var configuration = services.GetRequiredService<IInstanceConfigurationService>();
        var options = new InstanceConnectionOptions
        {
            TypeId = AliyunOssStorageProvider.TypeId,
            ProviderId = AliyunOssStorageProvider.ProviderId,
            Parameters = new Dictionary<string, string> { ["accessKeyId"] = "valid", ["accessKeySecret"] = "valid" },
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.CreateAsync(
            new InstanceConfigurationDraft { Name = "Account", Connection = options, SaveLocation = InstanceSaveLocation.Account },
            TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.CreateAsync(
            new InstanceConfigurationDraft
            {
                Name = "Unknown",
                Connection = new InstanceConnectionOptions { TypeId = "storage", ProviderId = "missing", Parameters = options.Parameters },
            }, TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(() => configuration.CreateAsync(
            new InstanceConfigurationDraft
            {
                Name = "Wrong type",
                Connection = new InstanceConnectionOptions { TypeId = "docker", ProviderId = options.ProviderId, Parameters = options.Parameters },
            }, TestContext.Current.CancellationToken));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => configuration.CreateAsync(
            new InstanceConfigurationDraft { Name = "Cancelled", Connection = options }, cancellation.Token));
        Assert.Empty(configuration.GetInstances());
    }
}
