using System.Security.Cryptography;
using System.Text.Json;
using Asgard.Hermod.Infrastructure.Security;
using Asgard.Hermod.Infrastructure.Storage;

namespace Asgard.Hermod.UI.Tests;

/// <summary>使用历史版本二结构验证通用实例迁移保留真实配置、设置与稳定关联。</summary>
public sealed class LocalDatabaseVersionThreeTests
{
    /// <summary>旧实例默认为存储类型，密文、日期、名称和标签标识均不能重写。</summary>
    [Fact]
    public async Task Initialize_UpgradesVersionTwoWithoutReplacingExistingConfiguration()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        IFreeSql database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("""
            CREATE TABLE app_setting (id TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL, update_time TEXT NOT NULL);
            INSERT INTO app_setting VALUES ('legacy-setting', 'saved-value', '2026-10-12');
            CREATE TABLE instance_configuration (
                id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, provider_id TEXT NOT NULL,
                protected_parameters BLOB NOT NULL, create_time TEXT NOT NULL);
            CREATE TABLE managed_tag (
                id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, name_key TEXT NOT NULL,
                parent_id TEXT NULL REFERENCES managed_tag(id), color TEXT NOT NULL,
                is_built_in INTEGER NOT NULL CHECK (is_built_in IN (0, 1)));
            CREATE UNIQUE INDEX managed_tag_sibling_name ON managed_tag(IFNULL(parent_id, ''), name_key);
            CREATE TABLE instance_tag (
                instance_id TEXT NOT NULL REFERENCES instance_configuration(id),
                tag_id TEXT NOT NULL REFERENCES managed_tag(id), PRIMARY KEY(instance_id, tag_id));
            CREATE INDEX instance_tag_tag_id ON instance_tag(tag_id);
            INSERT INTO managed_tag VALUES
                ('builtin.environment.production', 'Production', 'PRODUCTION', NULL, '#EF4444', 1),
                ('legacy-tag', 'Legacy team', 'LEGACY TEAM', NULL, '#AABBCC', 0);
            PRAGMA user_version = 2;
            """);
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string>
        {
            ["accessKeyId"] = "LegacyAccessKey123",
            ["accessKeySecret"] = "legacy-private-secret",
        });
        byte[] ciphertext;
        try { ciphertext = services.GetRequiredService<ISecretProtector>().Protect(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        const string createdAt = "2026-10-11T10:20:30.0000000Z";
        database.Ado.ExecuteNonQuery("""
            INSERT INTO instance_configuration VALUES ('legacy-instance', 'Legacy OSS', 'aliyun-oss', @parameters, @created);
            INSERT INTO instance_tag VALUES ('legacy-instance', 'legacy-tag');
            INSERT INTO instance_tag VALUES ('legacy-instance', 'builtin.environment.production');
            """, new { parameters = ciphertext, created = createdAt });

        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        Assert.Equal(LocalDatabaseInitializer.CurrentSchemaVersion, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal("saved-value", database.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'legacy-setting';"));
        Assert.Equal(ciphertext, (byte[])database.Ado.ExecuteScalar("SELECT protected_parameters FROM instance_configuration;"));
        Assert.Equal(createdAt, database.Ado.ExecuteScalar("SELECT create_time FROM instance_configuration;"));
        var configuration = services.GetRequiredService<IInstanceConfigurationService>();
        InstanceSummary instance = Assert.Single(configuration.GetInstances());
        Assert.Equal("legacy-instance", instance.Id);
        Assert.Equal("Legacy OSS", instance.Name);
        Assert.Equal(AliyunOssStorageProvider.TypeId, instance.TypeId);
        Assert.Equal(AliyunOssStorageProvider.ProviderId, instance.ProviderId);
        Assert.Contains("Legacy team", instance.Tags);
        Assert.Contains("Environment/Production", instance.Tags);
        InstanceConnectionOptions? recovered = await configuration.GetConnectionAsync(instance.Id, TestContext.Current.CancellationToken);
        Assert.NotNull(recovered);
        Assert.Equal("storage", recovered.TypeId);
        Assert.Equal("legacy-private-secret", recovered.Parameters["accessKeySecret"]);
        Assert.Equal(2, services.GetRequiredService<ILocalConfigurationRepository>()
            .GetInstance(instance.Id, TestContext.Current.CancellationToken)!.TagIds.Count);
        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        Assert.Single(configuration.GetInstances());
    }

    /// <summary>损坏版本二不能部分升级，失败保持旧版本号和已存密文。</summary>
    [Fact]
    public void Initialize_RollsBackVersionThreeWhenLegacyTablesAreMissing()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        IFreeSql database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("""
            CREATE TABLE app_setting (id TEXT NOT NULL PRIMARY KEY, value TEXT NOT NULL, update_time TEXT NOT NULL);
            CREATE TABLE instance_configuration (
                id TEXT NOT NULL PRIMARY KEY, name TEXT NOT NULL, provider_id TEXT NOT NULL,
                protected_parameters BLOB NOT NULL, create_time TEXT NOT NULL);
            INSERT INTO instance_configuration VALUES ('legacy', 'Legacy', 'aliyun-oss', X'010203', 'saved-date');
            PRAGMA user_version = 2;
            """);

        Assert.Throws<SQLiteException>(services.GetRequiredService<LocalDatabaseInitializer>().Initialize);

        Assert.Equal(2, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal(new byte[] { 1, 2, 3 }, (byte[])database.Ado.ExecuteScalar("SELECT protected_parameters FROM instance_configuration;"));
        Assert.Equal(0, Convert.ToInt32(database.Ado.ExecuteScalar(
            "SELECT COUNT(*) FROM pragma_table_info('instance_configuration') WHERE name = 'type_id';")));
    }
}
