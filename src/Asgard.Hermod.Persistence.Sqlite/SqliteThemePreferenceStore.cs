namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>复用 app_setting 保存稳定主题标识，不依赖 WPF 或数据库结构迁移。</summary>
public sealed class SqliteThemePreferenceStore(IFreeSql database) : IThemePreferenceStore
{
    /// <inheritdoc />
    public UiTheme? Load()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'ui.theme';";
        return (command.ExecuteScalar() as string) switch
        {
            "light" => UiTheme.Light,
            "dark" => UiTheme.Dark,
            _ => null,
        };
    }

    /// <inheritdoc />
    public void Save(UiTheme theme)
    {
        string themeId = theme switch
        {
            UiTheme.Light => "light",
            UiTheme.Dark => "dark",
            _ => throw new ArgumentOutOfRangeException(nameof(theme)),
        };
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('ui.theme', @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var value = command.CreateParameter();
        value.ParameterName = "@value";
        value.Value = themeId;
        command.Parameters.Add(value);
        var time = command.CreateParameter();
        time.ParameterName = "@time";
        time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
