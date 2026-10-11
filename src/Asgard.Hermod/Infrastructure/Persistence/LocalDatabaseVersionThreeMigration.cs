namespace Asgard.Hermod.Infrastructure.Persistence;

/// <summary>为旧存储实例补上类型标识，不重写密文、实例标识或标签关联。</summary>
internal static class LocalDatabaseVersionThreeMigration
{
    /// <summary>历史版本只支持存储，默认值保持旧实例的连接器语义。</summary>
    public static void Apply(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.CommandText = """
            ALTER TABLE instance_configuration ADD COLUMN type_id TEXT NOT NULL DEFAULT 'storage';
            PRAGMA user_version = 3;
            """;
        command.ExecuteNonQuery();
    }

    /// <summary>当前版本启动也验证类型字段，结构不完整时不允许进入保存流程。</summary>
    public static void Validate(DbCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.CommandText = "SELECT type_id FROM instance_configuration LIMIT 0;";
        using (command.ExecuteReader()) { }
    }
}
