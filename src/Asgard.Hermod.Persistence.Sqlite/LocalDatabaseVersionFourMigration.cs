namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>增加独立托管品牌，不改写已有连接器、凭据或标签。</summary>
internal static class LocalDatabaseVersionFourMigration
{
    /// <summary>历史 SSH 和数据库实例保持自部署默认值，存储连接器仍自行决定品牌。</summary>
    public static void Apply(DbCommand command)
    {
        command.CommandText = """
            ALTER TABLE instance_configuration ADD COLUMN hosting_provider_id TEXT NOT NULL DEFAULT '';
            PRAGMA user_version = 4;
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>启动时检查品牌列，损坏结构不能进入保存流程。</summary>
    public static void Validate(DbCommand command)
    {
        command.CommandText = "SELECT hosting_provider_id FROM instance_configuration LIMIT 0;";
        using (command.ExecuteReader()) { }
    }
}
