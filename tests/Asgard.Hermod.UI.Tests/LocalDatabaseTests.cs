namespace Asgard.Hermod.UI.Tests;

/// <summary>使用真实文件和生产服务注册验证持久化初始化、数据保留与失败边界。</summary>
public sealed class LocalDatabaseTests
{
    /// <summary>首次运行应自动创建父目录、数据库文件和本地设置表。</summary>
    [Fact]
    public void Initialize_CreatesDatabaseAndBaselineSchema()
    {
        using var environment = new LocalStorageTestEnvironment();
        var configuration = environment.CreateConfiguration("含空格的 data/hermod.db");
        Assert.False(Directory.Exists(environment.DirectoryPath));
        using var services = environment.CreateServices(configuration);

        services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        Assert.True(File.Exists(configuration.DatabasePath));
        var database = services.GetRequiredService<IFreeSql>();
        Assert.Equal(LocalDatabaseInitializer.CurrentSchemaVersion,
            Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal(0, Convert.ToInt32(database.Ado.ExecuteScalar("SELECT COUNT(*) FROM app_setting;")));
        Assert.False(database.CodeFirst.IsAutoSyncStructure);
    }

    /// <summary>重复初始化和重启必须保留用户写入的值与时间。</summary>
    [Fact]
    public void Initialize_PreservesSettingsAcrossRepeatedCallsAndRestart()
    {
        using var environment = new LocalStorageTestEnvironment();
        const string value = "{\"theme\":\"dark\",\"label\":\"本地设置\"}";
        const string timestamp = "2026-10-11T08:00:00.0000000Z";
        using (var services = environment.CreateServices())
        {
            var initializer = services.GetRequiredService<LocalDatabaseInitializer>();
            initializer.Initialize();
            services.GetRequiredService<IFreeSql>().Ado.ExecuteNonQuery(
                "INSERT INTO app_setting (id, value, update_time) VALUES (@id, @value, @timestamp);",
                new { id = "appearance", value, timestamp });
            initializer.Initialize();
        }

        using var restarted = environment.CreateServices();
        restarted.GetRequiredService<LocalDatabaseInitializer>().Initialize();
        var database = restarted.GetRequiredService<IFreeSql>();
        Assert.Equal(value, database.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = @id;",
            new { id = "appearance" }));
        Assert.Equal(timestamp, database.Ado.ExecuteScalar("SELECT update_time FROM app_setting WHERE id = @id;",
            new { id = "appearance" }));
        Assert.Equal(1, Convert.ToInt32(database.Ado.ExecuteScalar("SELECT COUNT(*) FROM app_setting;")));
    }

    /// <summary>较旧的程序不能重建或降级已经由新版程序升级的数据库。</summary>
    [Fact]
    public void Initialize_RejectsNewerSchemaWithoutReplacingData()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        var initializer = services.GetRequiredService<LocalDatabaseInitializer>();
        initializer.Initialize();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("INSERT INTO app_setting VALUES ('sentinel', 'saved', '2026-10-11');");
        database.Ado.ExecuteNonQuery("PRAGMA user_version = 2;");

        Assert.Throws<InvalidOperationException>(initializer.Initialize);

        Assert.Equal(2, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal("saved", database.Ado.ExecuteScalar("SELECT value FROM app_setting WHERE id = 'sentinel';"));
    }

    /// <summary>遇到未经版本管理的同名表时迁移失败，不能覆盖数据或标记成功。</summary>
    [Fact]
    public void Initialize_DoesNotStampVersionWhenMigrationFails()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("CREATE TABLE app_setting (id TEXT PRIMARY KEY, original_value TEXT);");
        database.Ado.ExecuteNonQuery("INSERT INTO app_setting VALUES ('sentinel', 'saved');");

        Assert.Throws<SQLiteException>(services.GetRequiredService<LocalDatabaseInitializer>().Initialize);

        Assert.Equal(0, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal("saved", database.Ado.ExecuteScalar("SELECT original_value FROM app_setting;"));
        // 失败后的事务应已回滚，连接仍可正常获得写锁。
        database.Ado.ExecuteNonQuery("INSERT INTO app_setting VALUES ('second', 'still writable');");
    }

    /// <summary>即使版本号匹配，缺少必要表时也不能进入可保存的应用状态。</summary>
    [Fact]
    public void Initialize_RejectsMissingTableInExistingVersion()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var services = environment.CreateServices();
        var database = services.GetRequiredService<IFreeSql>();
        database.Ado.ExecuteNonQuery("PRAGMA user_version = 1;");

        Assert.Throws<SQLiteException>(services.GetRequiredService<LocalDatabaseInitializer>().Initialize);

        Assert.Equal(1, Convert.ToInt32(database.Ado.ExecuteScalar("PRAGMA user_version;")));
    }

    /// <summary>多个应用容器同时启动时，只能串行执行首次结构迁移。</summary>
    [Fact]
    public async Task Initialize_SerializesConcurrentStartup()
    {
        using var environment = new LocalStorageTestEnvironment();
        using var first = environment.CreateServices();
        using var second = environment.CreateServices();
        await Task.WhenAll(
            Task.Run(() => first.GetRequiredService<LocalDatabaseInitializer>().Initialize(), TestContext.Current.CancellationToken),
            Task.Run(() => second.GetRequiredService<LocalDatabaseInitializer>().Initialize(), TestContext.Current.CancellationToken));

        Assert.Equal(1, Convert.ToInt32(first.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("PRAGMA user_version;")));
        Assert.Equal(0, Convert.ToInt32(second.GetRequiredService<IFreeSql>().Ado.ExecuteScalar("SELECT COUNT(*) FROM app_setting;")));
    }

    /// <summary>关闭应用容器必须释放 SQLite 文件句柄。</summary>
    [Fact]
    public void Dispose_ReleasesDatabaseFile()
    {
        using var environment = new LocalStorageTestEnvironment();
        var configuration = environment.CreateConfiguration();
        using (var services = environment.CreateServices(configuration))
            services.GetRequiredService<LocalDatabaseInitializer>().Initialize();

        using var exclusiveFile = File.Open(configuration.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(exclusiveFile.Length > 0);
    }

    /// <summary>无效文件也应报错并保留原始内容，不能当成空库重新初始化。</summary>
    [Fact]
    public void Initialize_RejectsCorruptFileWithoutReplacingIt()
    {
        using var environment = new LocalStorageTestEnvironment();
        var configuration = environment.CreateConfiguration();
        byte[] original = "invalid sqlite contents"u8.ToArray();
        using (var services = environment.CreateServices(configuration))
        {
            File.WriteAllBytes(configuration.DatabasePath, original);
            Assert.ThrowsAny<Exception>(() => services.GetRequiredService<LocalDatabaseInitializer>().Initialize());
        }

        Assert.Equal(original, File.ReadAllBytes(configuration.DatabasePath));
    }

    /// <summary>数据库相对路径默认定位到用户数据目录，不依赖当前工作目录。</summary>
    [Fact]
    public void Configuration_DefaultsToLocalApplicationData()
    {
        var configuration = new LocalDatabaseConfiguration(new DatabaseConfig
        {
            Enabled = true,
            Provider = "sqlite",
            ConnectionString = "Data Source=hermod.db;",
        });

        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Asgard", "Hermod", "hermod.db"), configuration.DatabasePath);
    }

    /// <summary>命令行可以覆盖随应用发布的 YAML 文件中的数据库路径。</summary>
    [Fact]
    public void Configuration_LoadsShippedYamlAndCommandLineOverride()
    {
        using var environment = new LocalStorageTestEnvironment();
        var configuration = LocalDatabaseConfiguration.Load(
            ["--database:enabled=true", "--database:provider=sqlite", "--database:connectionString=Data Source=custom/database.db;"],
            dataDirectory: environment.DirectoryPath);

        Assert.Equal(Path.Combine(environment.DirectoryPath, "custom", "database.db"), configuration.DatabasePath);
        Assert.Equal(configuration.DatabasePath, new SQLiteConnectionStringBuilder(configuration.Database.ConnectionString).DataSource);
        Assert.False(Directory.Exists(environment.DirectoryPath));
    }

    /// <summary>初始化前拒绝禁用、远程、内存或只读配置，避免用户误以为数据已经保存。</summary>
    [Theory]
    [InlineData(false, "sqlite", "Data Source=hermod.db;")]
    [InlineData(true, "mysql", "Data Source=hermod.db;")]
    [InlineData(true, "sqlite", "Data Source=:memory:;")]
    [InlineData(true, "sqlite", "Data Source=;")]
    [InlineData(true, "sqlite", "Data Source=hermod.db;Read Only=True;")]
    [InlineData(true, "sqlite", "Uri=file:hermod.db;")]
    public void Configuration_RejectsUnsupportedStorage(bool enabled, string provider, string connectionString)
    {
        using var environment = new LocalStorageTestEnvironment();

        Assert.Throws<InvalidOperationException>(() => new LocalDatabaseConfiguration(
            new DatabaseConfig { Enabled = enabled, Provider = provider, ConnectionString = connectionString },
            environment.DirectoryPath));

        Assert.False(Directory.Exists(environment.DirectoryPath));
    }
}
