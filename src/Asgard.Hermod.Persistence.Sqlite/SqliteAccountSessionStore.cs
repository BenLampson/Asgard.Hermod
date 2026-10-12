using System.Security.Cryptography;
using System.Text;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>会话使用当前用户 DPAPI 密文，app_setting 不保存明文 Token。</summary>
public sealed class SqliteAccountSessionStore(IFreeSql database, ISecretProtector protector) : IAccountSessionStore
{
    /// <inheritdoc />
    public string? LoadSession()
    {
        string? value = Read("identity.session");
        if (string.IsNullOrEmpty(value)) return null;
        byte[] plaintext = protector.Unprotect(Convert.FromBase64String(value));
        try { return Encoding.UTF8.GetString(plaintext); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <inheritdoc />
    public void SaveSession(string? session)
    {
        if (session is null) { Write("identity.session", ""); return; }
        byte[] plaintext = Encoding.UTF8.GetBytes(session);
        try { Write("identity.session", Convert.ToBase64String(protector.Protect(plaintext))); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
    /// <inheritdoc />
    public string LoadRevision(string owner) => Read("sync.revision." + owner) ?? "0";
    /// <inheritdoc />
    public void SaveRevision(string owner, string revision) => Write("sync.revision." + owner, revision);

    /// <summary>所有动态内容作为参数传入，身份与本地语言设置互不覆盖。</summary>
    private string? Read(string key)
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = @id;";
        Add(command, "@id", key);
        return command.ExecuteScalar() as string;
    }
    /// <summary>通过单条 upsert 原子提交受保护的会话或版本。</summary>
    private void Write(string key, string value)
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = """
            INSERT INTO app_setting (id, value, update_time) VALUES (@id, @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        Add(command, "@id", key); Add(command, "@value", value);
        Add(command, "@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }
    /// <summary>数据库参数不参与日志和显示。</summary>
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
