using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>将主机身份存入设备级 app_setting，不参与账户工作区同步。</summary>
public sealed class SqliteHostKeyTrustStore(IFreeSql database) : IHostKeyTrustStore
{
    /// <inheritdoc />
    public Task<TrustedHostKey?> LoadAsync(string host, int port, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = @id;";
        Add(command, "@id", Key(host, port));
        cancellationToken.ThrowIfCancellationRequested();
        return Read(command.ExecuteScalar(), host, port);
    }, cancellationToken);

    /// <inheritdoc />
    public Task<bool> TrustAsync(TrustedHostKey key, string? expectedFingerprint, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        Validate(key);
        using var lease = database.Ado.MasterPool.Get();
        using var transaction = lease.Value.BeginTransaction(IsolationLevel.Serializable);
        using var command = lease.Value.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT value FROM app_setting WHERE id = @id;";
        Add(command, "@id", Key(key.Host, key.Port));
        var current = Read(command.ExecuteScalar(), key.Host, key.Port);
        if (current?.Fingerprint != expectedFingerprint) return false;
        cancellationToken.ThrowIfCancellationRequested();
        command.CommandText = """
            INSERT INTO app_setting(id, value, update_time) VALUES(@id, @value, @time)
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        Add(command, "@value", JsonSerializer.Serialize(key));
        Add(command, "@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
        transaction.Commit();
        return true;
    }, cancellationToken);

    /// <summary>IPv6 和大小写域名共享同一身份边界，数据库键不包含用户输入原文。</summary>
    private static string Key(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        string normalized = IPAddress.TryParse(host, out var address) ? address.ToString()
            : new IdnMapping().GetAscii(host.TrimEnd('.')).ToLowerInvariant();
        return "ssh.host." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            normalized + ":" + port.ToString(CultureInfo.InvariantCulture))));
    }

    /// <summary>损坏的信任记录明确失败，不能被当成首次连接覆盖。</summary>
    private static TrustedHostKey? Read(object? value, string host, int port)
    {
        if (value is not string json) return null;
        try
        {
            var key = JsonSerializer.Deserialize<TrustedHostKey>(json) ?? throw Text.Exception("SshTrustUnavailable");
            Validate(key);
            if (Key(key.Host, key.Port) != Key(host, port)) throw Text.Exception("SshTrustUnavailable");
            return key;
        }
        catch (JsonException) { throw Text.Exception("SshTrustUnavailable"); }
    }

    /// <summary>拒绝把响应片段或控制字符存为公开身份。</summary>
    private static void Validate(TrustedHostKey key)
    {
        if (key.Fingerprint is null || !key.Fingerprint.StartsWith("SHA256:", StringComparison.Ordinal)
            || key.Fingerprint.Length != 50 || key.Fingerprint[7..].Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/')
            || string.IsNullOrWhiteSpace(key.Algorithm) || key.Algorithm.Length > 128 || key.Algorithm.Any(char.IsControl))
            throw Text.Exception("SshTrustUnavailable");
        _ = Key(key.Host, key.Port);
    }

    /// <summary>所有设置值使用参数化 SQL。</summary>
    private static void Add(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter(); parameter.ParameterName = name; parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
