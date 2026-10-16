using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Asgard.Hermod.Server;

/// <summary>逐条配置和旧快照迁移共用写事务，旧客户端不能覆盖迁移后的集合。</summary>
public sealed partial class SqliteCloudWorkspaceRepository
{
    /// <summary>版本二新增明细表，保留旧密文供客户端解密迁移。</summary>
    private static void InitializeConfiguration(SqliteCommand command)
    {
        command.CommandText = """
            CREATE TABLE sync_configuration (
                owner_key TEXT PRIMARY KEY NOT NULL, tenant_id TEXT NOT NULL, application_id TEXT NOT NULL,
                subject_id TEXT NOT NULL, revision TEXT NOT NULL, protection_json TEXT NOT NULL,
                create_time TEXT NOT NULL, update_time TEXT NOT NULL);
            CREATE TABLE sync_instance (
                owner_key TEXT NOT NULL, id TEXT NOT NULL, version TEXT NOT NULL, entry_json TEXT NOT NULL,
                create_time TEXT NOT NULL, update_time TEXT NOT NULL, PRIMARY KEY (owner_key, id));
            CREATE TABLE sync_tag (
                owner_key TEXT NOT NULL, id TEXT NOT NULL, version TEXT NOT NULL, entry_json TEXT NOT NULL,
                create_time TEXT NOT NULL, update_time TEXT NOT NULL, PRIMARY KEY (owner_key, id));
            PRAGMA user_version = 2;
            """;
        command.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public CloudConfiguration ReadConfiguration(string ownerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadConfiguration(connection, transaction, ownerKey);
        transaction.Commit(); return result;
    }

    /// <summary>集合头和明细在同一个快照读取。</summary>
    private static CloudConfiguration ReadConfiguration(SqliteConnection connection, SqliteTransaction transaction, string owner)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT revision, protection_json, update_time, agent_models_json FROM sync_configuration WHERE owner_key = @owner;";
        command.Parameters.AddWithValue("@owner", owner);
        string revision; EncryptedWorkspace? protection, agentModels; DateTime updated;
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read())
            {
                reader.Close();
                var legacy = Read(connection, transaction, owner);
                return new(legacy?.Revision ?? "0", null, [], [], legacy?.UpdatedAt,
                    legacy is null ? null : JsonSerializer.Deserialize<EncryptedWorkspace>(legacy.ProtectedJson));
            }
            revision = reader.GetString(0); protection = JsonSerializer.Deserialize<EncryptedWorkspace>(reader.GetString(1));
            updated = DateTime.Parse(reader.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            agentModels = reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<EncryptedWorkspace>(reader.GetString(3));
        }
        return new(revision, protection, ReadEntries<CloudInstanceConfiguration>(command, "sync_instance"),
            ReadEntries<CloudTagConfiguration>(command, "sync_tag"), updated)
        { AgentModels = agentModels };
    }

    /// <summary>表名只来自固定实现，账户参数始终参数化。</summary>
    private static IReadOnlyList<T> ReadEntries<T>(SqliteCommand command, string table)
    {
        command.CommandText = $"SELECT entry_json FROM {table} WHERE owner_key = @owner ORDER BY id;";
        var result = new List<T>(); using var reader = command.ExecuteReader();
        while (reader.Read()) result.Add(JsonSerializer.Deserialize<T>(reader.GetString(0))!);
        return result;
    }

    /// <inheritdoc />
    public CloudConfiguration? SaveConfiguration(string ownerKey, HermodAccount account, ConfigurationUpload upload, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Open(); using var transaction = connection.BeginTransaction();
        var previous = ReadConfiguration(connection, transaction, ownerKey);
        if (previous.Revision != upload.ExpectedRevision) return null;
        // 普通同步不能替换已有密码校验包，避免旧设备改变密码。
        if (previous.Protection is not null && previous.Protection != upload.Configuration.Protection) return null;
        // null 是旧客户端未提供该字段；明确清除以非空密文内的空集合表达。
        var agentModels = upload.Configuration.AgentModels ?? previous.AgentModels;
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        var instances = new List<CloudInstanceConfiguration>(); var tags = new List<CloudTagConfiguration>();
        foreach (var input in upload.Configuration.Instances)
        {
            var current = previous.Instances.FirstOrDefault(value => value.Id == input.Id);
            if ((current?.Version ?? "0") != input.Version) return null;
            var updated = current is null ? input with { Version = NewVersion() } : current with
            { Name = input.Name, Parameters = input.Parameters, Secrets = input.Secrets, TagIds = input.TagIds };
            if (current is not null && (current.TypeId != input.TypeId || current.ProviderId != input.ProviderId))
                throw new HermodApiException(400, "SyncSnapshotInvalid");
            if (current is not null && JsonSerializer.Serialize(updated) != JsonSerializer.Serialize(current))
                updated = updated with { Version = NewVersion() };
            instances.Add(updated);
        }
        foreach (var input in upload.Configuration.Tags)
        {
            var current = previous.Tags.FirstOrDefault(value => value.Tag.Id == input.Tag.Id);
            if ((current?.Version ?? "0") != input.Version) return null;
            tags.Add(new(input.Tag, current?.Tag == input.Tag ? current.Version : NewVersion()));
        }
        string now = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture), revision = NewVersion();
        command.CommandText = """
            INSERT INTO sync_configuration (owner_key, tenant_id, application_id, subject_id, revision, protection_json, create_time, update_time, agent_models_json)
            VALUES (@owner, @tenant, @app, @subject, @revision, @protection, @now, @now, @models)
            ON CONFLICT(owner_key) DO UPDATE SET revision = excluded.revision, protection_json = excluded.protection_json, update_time = excluded.update_time, agent_models_json = excluded.agent_models_json;
            """;
        command.Parameters.AddWithValue("@owner", ownerKey); command.Parameters.AddWithValue("@tenant", account.TenantId);
        command.Parameters.AddWithValue("@app", account.ApplicationId); command.Parameters.AddWithValue("@subject", account.SubjectId);
        command.Parameters.AddWithValue("@revision", revision); command.Parameters.AddWithValue("@protection", JsonSerializer.Serialize(upload.Configuration.Protection));
        command.Parameters.AddWithValue("@models", agentModels is null ? DBNull.Value : JsonSerializer.Serialize(agentModels));
        command.Parameters.AddWithValue("@now", now); command.ExecuteNonQuery();
        SaveEntries(command, "sync_instance", instances, item => item.Id, item => item.Version, now);
        SaveEntries(command, "sync_tag", tags, item => item.Tag.Id, item => item.Version, now);
        // 成功迁移后旧整包不再可读。
        command.Parameters.Clear(); command.Parameters.AddWithValue("@owner", ownerKey);
        command.CommandText = "DELETE FROM sync_workspace WHERE owner_key = @owner;"; command.ExecuteNonQuery();
        cancellationToken.ThrowIfCancellationRequested(); transaction.Commit();
        return new(revision, upload.Configuration.Protection, instances, tags, DateTime.Parse(now, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)) { AgentModels = agentModels };
    }

    /// <summary>逐条 upsert 保留服务器创建审计，缺失条目在同一事务中删除。</summary>
    private static void SaveEntries<T>(SqliteCommand command, string table, IReadOnlyList<T> entries,
        Func<T, string> id, Func<T, string> version, string now)
    {
        string owner = (string)command.Parameters["@owner"].Value!;
        var keep = entries.Select(id).ToHashSet(StringComparer.Ordinal);
        command.Parameters.Clear(); command.Parameters.AddWithValue("@owner", owner);
        command.CommandText = $"SELECT id FROM {table} WHERE owner_key = @owner;";
        var removed = new List<string>();
        using (var reader = command.ExecuteReader()) while (reader.Read()) if (!keep.Contains(reader.GetString(0))) removed.Add(reader.GetString(0));
        foreach (string deleted in removed)
        {
            command.Parameters.Clear(); command.Parameters.AddWithValue("@owner", owner); command.Parameters.AddWithValue("@id", deleted);
            command.CommandText = $"DELETE FROM {table} WHERE owner_key = @owner AND id = @id;"; command.ExecuteNonQuery();
        }
        foreach (var entry in entries)
        {
            command.Parameters.Clear(); command.Parameters.AddWithValue("@owner", owner); command.Parameters.AddWithValue("@id", id(entry));
            command.Parameters.AddWithValue("@version", version(entry)); command.Parameters.AddWithValue("@json", JsonSerializer.Serialize(entry));
            command.Parameters.AddWithValue("@now", now);
            command.CommandText = $"""
                INSERT INTO {table} (owner_key, id, version, entry_json, create_time, update_time) VALUES (@owner, @id, @version, @json, @now, @now)
                ON CONFLICT(owner_key, id) DO UPDATE SET version = excluded.version, entry_json = excluded.entry_json, update_time = excluded.update_time
                WHERE version != excluded.version;
                """;
            command.ExecuteNonQuery();
        }
    }
    /// <summary>版本只有相等比较语义，不表示时间或修改次数。</summary>
    private static string NewVersion() => Guid.NewGuid().ToString("N");
}
