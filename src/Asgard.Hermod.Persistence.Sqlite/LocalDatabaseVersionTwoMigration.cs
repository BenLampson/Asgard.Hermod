namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>从设置表基线升级到真实实例与稳定标签；由初始化事务统一提交或回滚。</summary>
internal static class LocalDatabaseVersionTwoMigration
{
    /// <summary>新增表和固定环境标签后才推进版本号，不重写版本一的基线。</summary>
    public static void Apply(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.CommandText = """
            CREATE TABLE instance_configuration (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                provider_id TEXT NOT NULL,
                protected_parameters BLOB NOT NULL,
                create_time TEXT NOT NULL
            );
            CREATE TABLE managed_tag (
                id TEXT NOT NULL PRIMARY KEY,
                name TEXT NOT NULL,
                name_key TEXT NOT NULL,
                parent_id TEXT NULL REFERENCES managed_tag(id),
                color TEXT NOT NULL,
                is_built_in INTEGER NOT NULL CHECK (is_built_in IN (0, 1))
            );
            CREATE UNIQUE INDEX managed_tag_sibling_name
                ON managed_tag(IFNULL(parent_id, ''), name_key);
            CREATE TABLE instance_tag (
                instance_id TEXT NOT NULL REFERENCES instance_configuration(id),
                tag_id TEXT NOT NULL REFERENCES managed_tag(id),
                PRIMARY KEY (instance_id, tag_id)
            );
            CREATE INDEX instance_tag_tag_id ON instance_tag(tag_id);
            INSERT INTO managed_tag VALUES
                ('builtin.environment.production', 'Production', 'PRODUCTION', NULL, '#EF4444', 1),
                ('builtin.environment.staging', 'Staging', 'STAGING', NULL, '#F59E0B', 1),
                ('builtin.environment.development', 'Development', 'DEVELOPMENT', NULL, '#22C55E', 1);
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>已有版本二也验证必要列，避免缺失结构导致保存时才报错。</summary>
    public static void Validate(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        foreach (string query in new[]
        {
            "SELECT id, name, provider_id, protected_parameters, create_time FROM instance_configuration LIMIT 0;",
            "SELECT id, name, name_key, parent_id, color, is_built_in FROM managed_tag LIMIT 0;",
            "SELECT instance_id, tag_id FROM instance_tag LIMIT 0;",
        })
        {
            command.CommandText = query;
            using (command.ExecuteReader()) { }
        }
    }
}
