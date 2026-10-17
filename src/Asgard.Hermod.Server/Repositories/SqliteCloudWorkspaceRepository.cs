using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Asgard.Hermod.Server;

/// <summary>独立 SQLite WAL 文件；写锁覆盖先查后改，可跨进程拒绝过期设备覆盖。</summary>
public sealed partial class SqliteCloudWorkspaceRepository(IOptions<HermodServerOptions> options) : ICloudWorkspaceRepository
{
    /// <inheritdoc />
    public void Initialize()
    {
        string path = Path.GetFullPath(options.Value.DatabasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "PRAGMA user_version;";
        long version = (long)command.ExecuteScalar()!;
        if (version > 3) throw Text.Exception("SyncDatabaseVersionUnsupported");
        if (version == 0)
        {
            command.CommandText = """
                CREATE TABLE sync_workspace (
                    owner_key TEXT PRIMARY KEY NOT NULL,
                    tenant_id TEXT NOT NULL, application_id TEXT NOT NULL, subject_id TEXT NOT NULL,
                    revision TEXT NOT NULL, protected_json TEXT NOT NULL,
                    create_time TEXT NOT NULL, update_time TEXT NOT NULL
                );
                PRAGMA user_version = 1;
                """;
            command.ExecuteNonQuery();
        }
        if (version < 2) InitializeConfiguration(command);
        if (version < 3)
        {
            command.CommandText = "ALTER TABLE sync_configuration ADD COLUMN agent_models_json TEXT NULL; PRAGMA user_version = 3;";
            command.ExecuteNonQuery();
        }
        command.CommandText = "SELECT owner_key, tenant_id, application_id, subject_id, revision, protected_json, create_time, update_time FROM sync_workspace LIMIT 0;";
        using (command.ExecuteReader()) { }
        transaction.Commit();
    }
    /// <inheritdoc />
    public StoredCloudWorkspace? Read(string ownerKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Open();
        return Read(connection, null, ownerKey);
    }
    /// <inheritdoc />
    public StoredCloudWorkspace? Save(string ownerKey, HermodAccount account, string expectedRevision, string encryptedJson, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        var previous = Read(connection, transaction, ownerKey);
        using (var check = connection.CreateCommand())
        {
            check.Transaction = transaction;
            check.CommandText = "SELECT 1 FROM sync_configuration WHERE owner_key = @owner;";
            check.Parameters.AddWithValue("@owner", ownerKey);
            if (check.ExecuteScalar() is not null) return null;
        }
        if ((previous?.Revision ?? "0") != expectedRevision) return null;
        // 稳定归属永不被上传载荷覆盖，更新保留原创建时间和当前版本来源。
        if (previous is not null && (previous.TenantId != account.TenantId || previous.ApplicationId != account.ApplicationId || previous.SubjectId != account.SubjectId))
            throw new HermodApiException(403, "SyncForbidden");
        DateTime now = DateTime.UtcNow;
        var current = new StoredCloudWorkspace(ownerKey, account.TenantId, account.ApplicationId, account.SubjectId,
            Guid.NewGuid().ToString("N"), encryptedJson, previous?.CreatedAt ?? now, now);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO sync_workspace (owner_key, tenant_id, application_id, subject_id, revision, protected_json, create_time, update_time)
            VALUES (@owner, @tenant, @app, @sub, @revision, @json, @created, @updated)
            ON CONFLICT(owner_key) DO UPDATE SET revision = excluded.revision, protected_json = excluded.protected_json, update_time = excluded.update_time;
            """;
        command.Parameters.AddWithValue("@owner", current.OwnerKey); command.Parameters.AddWithValue("@tenant", current.TenantId);
        command.Parameters.AddWithValue("@app", current.ApplicationId); command.Parameters.AddWithValue("@sub", current.SubjectId);
        command.Parameters.AddWithValue("@revision", current.Revision); command.Parameters.AddWithValue("@json", current.ProtectedJson);
        command.Parameters.AddWithValue("@created", current.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("@updated", now.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery(); cancellationToken.ThrowIfCancellationRequested(); transaction.Commit();
        return current;
    }
    /// <summary>只读取服务器计算的分区标识，无法通过路径枚举其他账号。</summary>
    private static StoredCloudWorkspace? Read(SqliteConnection connection, SqliteTransaction? transaction, string ownerKey)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT owner_key, tenant_id, application_id, subject_id, revision, protected_json, create_time, update_time FROM sync_workspace WHERE owner_key = @owner;";
        command.Parameters.AddWithValue("@owner", ownerKey);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            reader.GetString(4), reader.GetString(5), DateTime.Parse(reader.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)) : null;
    }
    /// <summary>连接参数由服务器选定，不能来自用户请求。</summary>
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = Path.GetFullPath(options.Value.DatabasePath), DefaultTimeout = 30, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand(); command.CommandText = "PRAGMA journal_mode = WAL;"; command.ExecuteNonQuery();
        return connection;
    }
}
