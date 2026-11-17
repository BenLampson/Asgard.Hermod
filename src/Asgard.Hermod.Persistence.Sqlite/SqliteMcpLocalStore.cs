using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>读取固定工作区和连接版本，并使用 Windows 用户保护封存本机授权。</summary>
public sealed class SqliteMcpLocalStore(IFreeSql database, ISecretProtector protector) : IMcpLocalStore
{
    private const string OwnerQuery = "COALESCE((SELECT value FROM app_setting WHERE id = 'sync.activeOwner'), '')";

    /// <inheritdoc />
    public McpWorkspace ReadWorkspace()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = $"""
            SELECT {OwnerQuery}, i.id, i.name, i.type_id, i.protected_parameters, i.provider_id, i.create_time
            FROM (SELECT 1) LEFT JOIN instance_configuration AS i ON 1 = 1 ORDER BY i.name, i.id;
            """;
        using var reader = command.ExecuteReader();
        List<McpTarget> targets = [];
        string owner = "";
        while (reader.Read())
        {
            owner = reader.GetString(0);
            if (!reader.IsDBNull(1))
                targets.Add(new(reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    Revision((byte[])reader.GetValue(4), reader.GetString(5), reader.GetString(6))));
        }
        return new(owner, targets.AsReadOnly());
    }

    /// <inheritdoc />
    public string ReadOwner()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = $"SELECT {OwnerQuery};";
        return (string)command.ExecuteScalar()!;
    }

    /// <inheritdoc />
    public McpLocalPolicy? ReadPolicy()
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = "SELECT value FROM app_setting WHERE id = 'mcp.localPolicy';";
        if (command.ExecuteScalar() is not string { Length: > 0 } value) return null;
        byte[] plaintext = protector.Unprotect(Convert.FromBase64String(value));
        try { return JsonSerializer.Deserialize<McpLocalPolicy>(plaintext) ?? throw Text.Exception("McpAccessUnavailable"); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    /// <inheritdoc />
    public void SavePolicy(McpLocalPolicy policy)
    {
        byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(policy);
        string ciphertext;
        try { ciphertext = Convert.ToBase64String(protector.Protect(plaintext)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }

        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = $"""
            INSERT INTO app_setting (id, value, update_time)
            SELECT 'mcp.localPolicy', @ciphertext, @time WHERE {OwnerQuery} = @owner
            ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
            """;
        Add(command, "@ciphertext", ciphertext);
        Add(command, "@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        Add(command, "@owner", policy.Owner);
        if (command.ExecuteNonQuery() != 1) throw Text.Exception("McpWorkspaceChanged");
    }

    /// <inheritdoc />
    public InstanceConnectionOptions? OpenTarget(string owner, McpGrant grant)
    {
        var target = FindTarget(owner, grant);
        if (target is null) return null;
        byte[] plaintext = protector.Unprotect(target.Value.ProtectedParameters);
        try
        {
            var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext) ?? throw Text.Exception("McpAccessUnavailable");
            return new() { TypeId = grant.TypeId, ProviderId = target.Value.ProviderId, Parameters = new ReadOnlyDictionary<string, string>(parameters) };
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    /// <inheritdoc />
    public bool IsCurrent(string owner, McpGrant grant) => FindTarget(owner, grant) is not null;

    /// <summary>复用同一语句的归属快照；旧授权绝不能读取新连接的凭据。</summary>
    private (string ProviderId, byte[] ProtectedParameters)? FindTarget(string owner, McpGrant grant)
    {
        using var lease = database.Ado.MasterPool.Get();
        using var command = lease.Value.CreateCommand();
        command.CommandText = $"""
            SELECT provider_id, protected_parameters, create_time FROM instance_configuration
            WHERE id = @id AND type_id = @type AND {OwnerQuery} = @owner;
            """;
        Add(command, "@id", grant.InstanceId);
        Add(command, "@type", grant.TypeId);
        Add(command, "@owner", owner);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;
        string provider = reader.GetString(0);
        byte[] ciphertext = (byte[])reader.GetValue(1);
        return Revision(ciphertext, provider, reader.GetString(2)) == grant.Revision ? (provider, ciphertext) : null;
    }

    /// <summary>仅对密文和连接身份计算版本；名称、标签变化不影响授权。</summary>
    private static string Revision(byte[] ciphertext, string provider, string created) =>
        Convert.ToHexString(SHA256.HashData(ciphertext)) + ":" + provider + ":" + created;

    private static void Add(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
