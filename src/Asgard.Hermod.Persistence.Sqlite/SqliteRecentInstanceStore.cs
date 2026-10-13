using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>最近访问复用本机 app_setting，无需结构迁移或同步实例密钥。</summary>
public sealed class SqliteRecentInstanceStore(IFreeSql database) : IRecentInstanceStore
{
    /// <inheritdoc />
    public IReadOnlyList<RecentInstance> Load()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'workspace.recent';";
        if (command.ExecuteScalar() is not string { Length: > 0 } value) return [];
        try
        {
            return (JsonSerializer.Deserialize<RecentInstance[]>(value) ?? [])
                .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.InstanceId))
                .OrderByDescending(item => item.OpenedAt).DistinctBy(item => item.InstanceId).Take(5).ToArray();
        }
        catch (JsonException) { return []; }
    }

    /// <inheritdoc />
    public void Save(IReadOnlyList<RecentInstance> instances)
    {
        ArgumentNullException.ThrowIfNull(instances);
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('workspace.recent', @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var value = command.CreateParameter();
        value.ParameterName = "@value";
        value.Value = JsonSerializer.Serialize(instances.Take(5).ToArray());
        command.Parameters.Add(value);
        var time = command.CreateParameter();
        time.ParameterName = "@time";
        time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
