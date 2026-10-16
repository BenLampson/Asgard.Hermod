using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>模型集合整体使用当前 Windows 用户保护；设置表只保存密文，不保存明文 API Key。</summary>
public sealed class SqliteAgentModelStore(IFreeSql database, ISecretProtector protector) : IAgentModelStore
{
    /// <inheritdoc />
    public event EventHandler? Changed;
    /// <inheritdoc />
    public AgentModelSet? Load()
    {
        using var lease = database.Ado.MasterPool.Get(); using var command = lease.Value.CreateCommand();
        return Decode(ReadProtected(command), protector);
    }
    /// <inheritdoc />
    public void Save(AgentModelSet models)
    {
        string value = Encode(models, protector);
        using (var lease = database.Ado.MasterPool.Get())
        {
            using var transaction = lease.Value.BeginTransaction(IsolationLevel.Serializable);
            using var command = lease.Value.CreateCommand(); command.Transaction = transaction;
            WriteProtected(command, value);
            command.Parameters.Clear(); command.CommandText = "DELETE FROM app_setting WHERE id = 'agent.model';"; command.ExecuteNonQuery();
            transaction.Commit();
        }
        NotifyChanged();
    }
    /// <summary>同步恢复提交完成后触发，不在数据库租约或事务中调用订阅者。</summary>
    internal void NotifyChanged() => Changed?.Invoke(this, EventArgs.Empty);
    /// <summary>和实例、标签共享同一个快照事务读取。</summary>
    internal static string? ReadProtected(DbCommand command)
    {
        command.Parameters.Clear(); command.CommandText = "SELECT value FROM app_setting WHERE id = 'agent.models';";
        object? value = command.ExecuteScalar();
        return value is null ? null : value as string ?? throw Text.Exception("AgentModelUnlockFailed");
    }
    /// <summary>先验证再序列化，临时明文字节使用后清除。</summary>
    internal static string Encode(AgentModelSet models, ISecretProtector protector)
    {
        AgentModelValidation.Validate(models); byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(models);
        try
        {
            if (plaintext.Length > 1024 * 1024) throw Text.Exception("AgentModelDataInvalid");
            return Convert.ToBase64String(protector.Protect(plaintext));
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>解密错误不得退回空集合并覆盖原密文。</summary>
    internal static AgentModelSet? Decode(string? value, ISecretProtector protector)
    {
        if (value is null) return null;
        byte[]? plaintext = null;
        try
        {
            if (value.Length > 2 * 1024 * 1024) throw Text.Exception("AgentModelDataInvalid");
            plaintext = protector.Unprotect(Convert.FromBase64String(value));
            if (plaintext.Length > 1024 * 1024) throw Text.Exception("AgentModelDataInvalid");
            var models = JsonSerializer.Deserialize<AgentModelSet>(plaintext) ?? throw Text.Exception("AgentModelDataInvalid");
            AgentModelValidation.Validate(models); return models.Freeze();
        }
        catch (Exception) { throw Text.Exception("AgentModelUnlockFailed"); }
        finally { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <summary>外层事务在验证和设备保护成功后原子替换设置。</summary>
    internal static void WriteProtected(DbCommand command, string value)
    {
        command.Parameters.Clear(); command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES ('agent.models', @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        var data = command.CreateParameter(); data.ParameterName = "@value"; data.Value = value; command.Parameters.Add(data);
        var time = command.CreateParameter(); time.ParameterName = "@time"; time.Value = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture); command.Parameters.Add(time);
        command.ExecuteNonQuery();
    }
}
