namespace Asgard.Hermod.Infrastructure.Persistence;

/// <summary>在同一事务内初始化 SQLite 表结构与应用版本，重复启动保留已有数据。</summary>
public sealed class LocalDatabaseInitializer
{
    /// <summary>当前程序支持的数据库结构版本；新增迁移时递增。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>由服务容器拥有的业务数据库实例。</summary>
    private readonly IFreeSql _database;

    /// <summary>复用 Asgard 注册的数据库，避免另外创建独立连接池。</summary>
    public LocalDatabaseInitializer(IFreeSql database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <summary>获得写锁后检查版本；建表失败时回滚，未来版本拒绝由旧程序修改。</summary>
    public void Initialize()
    {
        using var lease = _database.Ado.MasterPool.Get();
        var connection = lease.Value;
        // SQLite 的 Serializable 事务立即获取写锁，多个进程不会同时执行首次迁移。
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        int version = Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version < 0 || version > CurrentSchemaVersion)
            throw new InvalidOperationException(
                $"Database schema version {version} is unsupported. This version of Hermod supports schema {CurrentSchemaVersion}.");

        if (version == 0)
        {
            // 本地设置是用户级基础设施数据，不带远程租户、账号或凭据。
            command.CommandText = """
                CREATE TABLE app_setting (
                    id TEXT NOT NULL PRIMARY KEY,
                    value TEXT NOT NULL,
                    update_time TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
            command.CommandText = "PRAGMA user_version = 1;";
            command.ExecuteNonQuery();
        }

        // 已有版本也验证必要列，避免版本标记存在但结构缺失时进入主界面。
        command.CommandText = "SELECT id, value, update_time FROM app_setting LIMIT 0;";
        using (command.ExecuteReader()) { }
        transaction.Commit();
    }
}
