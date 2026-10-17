using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>密码、基准和账户离线副本整体使用当前 Windows 用户保护。</summary>
public sealed class SqliteConfigurationSyncStateStore(IFreeSql database, ISecretProtector protector) : IConfigurationSyncStateStore
{
    /// <inheritdoc />
    public string ActiveOwner { get => Read("sync.activeOwner") ?? ""; set => Write("sync.activeOwner", value); }
    /// <inheritdoc />
    public ConfigurationSyncState Load(string owner)
    {
        string? value = Read("sync.state." + owner);
        if (string.IsNullOrEmpty(value)) return new();
        byte[] plaintext = protector.Unprotect(Convert.FromBase64String(value));
        try { return JsonSerializer.Deserialize<ConfigurationSyncState>(plaintext) ?? throw Text.Exception("SyncStateInvalid"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <inheritdoc />
    public void Save(string owner, ConfigurationSyncState state)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
        try { Write("sync.state." + owner, Convert.ToBase64String(protector.Protect(plaintext))); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>固定设置键和参数化值。</summary>
    private string? Read(string key)
    {
        using var lease = database.Ado.MasterPool.Get(); using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = @id;"; Add(command, "@id", key);
        return command.ExecuteScalar() as string;
    }
    /// <summary>单条 SQL 原子保存受保护状态。</summary>
    private void Write(string key, string value)
    {
        using var lease = database.Ado.MasterPool.Get(); using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES (@id, @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        Add(command, "@id", key); Add(command, "@value", value); Add(command, "@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
    /// <summary>秘密不参与 SQL 文本。</summary>
    private static void Add(DbCommand command, string key, string value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = key; parameter.Value = value; command.Parameters.Add(parameter);
    }
}
