namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>复用既有 app_setting 表与连接池，语言设置不需要数据库结构迁移。</summary>
public sealed class SqliteLanguagePreferenceStore(IFreeSql database) : ILanguagePreferenceStore
{
    /// <inheritdoc />
    public string? Load()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'ui.language';";
        return command.ExecuteScalar() as string;
    }

    /// <inheritdoc />
    public void Save(string? languageId)
    {
        if (languageId is not null && !Text.Languages.Any(language => language.Id == languageId))
            throw new ArgumentOutOfRangeException(nameof(languageId));
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('ui.language', @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var value = command.CreateParameter();
        value.ParameterName = "@value";
        value.Value = languageId ?? "";
        command.Parameters.Add(value);
        var time = command.CreateParameter();
        time.ParameterName = "@time";
        time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
