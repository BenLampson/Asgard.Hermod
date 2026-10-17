using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>模型偏好复用本地设置表，不进入实例配置同步通道。</summary>
public sealed class SqliteAgentPreferenceStore(IFreeSql database) : IAgentPreferenceStore
{
    /// <inheritdoc />
    public AgentPreferences Load()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'agent.model';";
        return command.ExecuteScalar() is string json
            ? JsonSerializer.Deserialize<AgentPreferences>(json) ?? new() : new();
    }

    /// <inheritdoc />
    public void Save(AgentPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('agent.model', @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var value = command.CreateParameter(); value.ParameterName = "@value";
        value.Value = JsonSerializer.Serialize(preferences); command.Parameters.Add(value);
        var time = command.CreateParameter(); time.ParameterName = "@time";
        time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
