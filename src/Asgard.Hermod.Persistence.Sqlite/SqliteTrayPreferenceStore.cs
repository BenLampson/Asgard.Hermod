namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>复用 app_setting 存储托盘入口提示标记，不引入数据库迁移。</summary>
public sealed class SqliteTrayPreferenceStore(IFreeSql database) : ITrayPreferenceStore
{
    /// <inheritdoc />
    public bool HasShownHideHint()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'ui.tray.hideHintShown';";
        return command.ExecuteScalar() as string == "1";
    }

    /// <inheritdoc />
    public void MarkHideHintShown()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('ui.tray.hideHintShown', '1', @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var time = command.CreateParameter();
        time.ParameterName = "@time";
        time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
