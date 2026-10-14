using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>同一 app_setting 值原子保存询问与动作，使用稳定协议标识并兼容缺失设置。</summary>
public sealed class SqliteMainWindowClosePreferenceStore(IFreeSql database) : IMainWindowClosePreferenceStore
{
    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public MainWindowClosePreference Load()
    {
        using var lease = database.Ado.MasterPool.Get(); using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id='ui.mainWindow.close';";
        if (command.ExecuteScalar() is not string value) return new();
        try
        {
            using var json = JsonDocument.Parse(value);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new();
            if (!root.TryGetProperty("ask", out var ask) || ask.ValueKind is not (JsonValueKind.True or JsonValueKind.False)
                || !root.TryGetProperty("action", out var action) || action.ValueKind != JsonValueKind.String) return new();
            return action.GetString() switch
            {
                "hide" => new(ask.GetBoolean(), MainWindowCloseAction.HideToTray),
                "exit" => new(ask.GetBoolean(), MainWindowCloseAction.ExitApplication),
                _ => new()
            };
        }
        catch (JsonException) { return new(); }
    }
    /// <inheritdoc />
    public void Save(MainWindowClosePreference preference)
    {
        ArgumentNullException.ThrowIfNull(preference);
        if (!Enum.IsDefined(preference.Action)) throw new ArgumentOutOfRangeException(nameof(preference));
        using var lease = database.Ado.MasterPool.Get(); using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting(id,value,update_time) VALUES('ui.mainWindow.close',@value,@time)
            ON CONFLICT(id) DO UPDATE SET value=excluded.value,update_time=excluded.update_time;
            """;
        var value = command.CreateParameter(); value.ParameterName = "@value";
        value.Value = JsonSerializer.Serialize(new { ask = preference.AskBeforeClosing, action = preference.Action == MainWindowCloseAction.HideToTray ? "hide" : "exit" });
        var time = command.CreateParameter(); time.ParameterName = "@time"; time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        command.Parameters.Add(value); command.Parameters.Add(time); command.ExecuteNonQuery();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
