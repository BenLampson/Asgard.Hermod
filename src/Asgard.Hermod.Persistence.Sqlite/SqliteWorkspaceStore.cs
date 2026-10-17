using System.Security.Cryptography;
using System.Text.Json;

namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>快照读取使用同一仓储事务；恢复在单个写事务内重新保护全部参数。</summary>
public sealed class SqliteWorkspaceStore(IFreeSql database, ILocalConfigurationRepository repository,
    ISecretProtector protector, LocalTagManagementService tags, SqliteAgentModelStore? agentModels = null) : ILocalWorkspaceStore
{
    /// <inheritdoc />
    public void Validate(WorkspaceSnapshot snapshot) => WorkspaceSnapshotValidation.Validate(snapshot);
    /// <inheritdoc />
    public Task<WorkspaceSnapshot> ExportAsync(CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var snapshot = repository.GetSnapshot(cancellationToken);
        return ToSnapshot(snapshot, cancellationToken);
    }, cancellationToken);

    /// <summary>事务读取结果转为明文内存快照，不写日志。</summary>
    private WorkspaceSnapshot ToSnapshot(LocalConfigurationSnapshot snapshot, CancellationToken cancellationToken)
    {
        var instances = snapshot.Instances.Select(instance =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] plaintext = protector.Unprotect(instance.ProtectedParameters);
            try
            {
                var parameters = JsonSerializer.Deserialize<Dictionary<string, string>>(plaintext)
                    ?? throw Text.Exception("SyncSnapshotInvalid");
                return new SyncedInstance(instance.Id, instance.Name, instance.TypeId, instance.ProviderId, parameters,
                    instance.CreatedAt.ToUniversalTime(), instance.TagIds)
                { HostingProviderId = instance.HostingProviderId };
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }).ToArray();
        return new WorkspaceSnapshot(1, instances, snapshot.Tags.Select(tag =>
            new SyncedTag(tag.Id, tag.Name, tag.ParentId, tag.Color, tag.IsBuiltIn)).ToArray())
        { AgentModels = SqliteAgentModelStore.Decode(snapshot.ProtectedAgentModels, protector) ?? AgentModelSet.Empty };
    }

    /// <inheritdoc />
    public async Task RestoreAsync(WorkspaceSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Restore(snapshot, cancellationToken), cancellationToken).ConfigureAwait(false);
        tags.NotifyTagsChanged();
        agentModels?.NotifyChanged();
    }

    /// <inheritdoc />
    public async Task RestoreIfUnchangedAsync(WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Restore(snapshot, cancellationToken, expected), cancellationToken).ConfigureAwait(false);
        tags.NotifyTagsChanged();
        agentModels?.NotifyChanged();
    }
    /// <inheritdoc />
    public async Task SwitchOwnerAsync(string owner, WorkspaceSnapshot snapshot, WorkspaceSnapshot expected, CancellationToken cancellationToken = default)
    {
        await Task.Run(() => Restore(snapshot, cancellationToken, expected, owner), cancellationToken).ConfigureAwait(false);
        tags.NotifyTagsChanged();
        agentModels?.NotifyChanged();
    }

    /// <summary>先完成验证和 DPAPI 保护，任何失败都不删除原配置。</summary>
    private void Restore(WorkspaceSnapshot snapshot, CancellationToken cancellationToken, WorkspaceSnapshot? expected = null, string? owner = null)
    {
        var orderedTags = WorkspaceSnapshotValidation.Validate(snapshot);
        string protectedModels = SqliteAgentModelStore.Encode(snapshot.AgentModels, protector);
        var protectedInstances = snapshot.Instances.Select(instance =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte[] plaintext = JsonSerializer.SerializeToUtf8Bytes(instance.Parameters);
            try
            {
                return new StoredInstanceConfiguration(instance.Id, instance.Name, instance.TypeId, instance.ProviderId,
                protector.Protect(plaintext), instance.CreatedAt, instance.TagIds)
                { HostingProviderId = instance.HostingProviderId };
            }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
        }).ToArray();
        using var lease = database.Ado.MasterPool.Get();
        using var transaction = lease.Value.BeginTransaction(IsolationLevel.Serializable);
        using var command = lease.Value.CreateCommand();
        command.Transaction = transaction;
        if (expected is not null && !WorkspaceContent.Equal(expected,
            ToSnapshot(SqliteConfigurationRepository.ReadSnapshot(command, cancellationToken), cancellationToken)))
            throw Text.Exception("SyncLocalChanged");
        Set(command, "DELETE FROM instance_tag; DELETE FROM instance_configuration; DELETE FROM managed_tag;");
        command.ExecuteNonQuery();
        foreach (var tag in orderedTags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(command, "INSERT INTO managed_tag (id, name, name_key, parent_id, color, is_built_in) VALUES (@id, @name, @key, @parent, @color, @builtin);",
                ("@id", tag.Id), ("@name", tag.Name), ("@key", TagHierarchy.NameKey(tag.Name)),
                ("@parent", tag.ParentId), ("@color", tag.Color), ("@builtin", tag.IsBuiltIn ? 1 : 0));
            command.ExecuteNonQuery();
        }
        foreach (var instance in protectedInstances)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Set(command, """
                INSERT INTO instance_configuration (id, name, type_id, provider_id, protected_parameters, create_time, hosting_provider_id)
                VALUES (@id, @name, @type, @provider, @parameters, @time, @hosting);
                """, ("@id", instance.Id), ("@name", instance.Name), ("@type", instance.TypeId),
                ("@provider", instance.ProviderId), ("@parameters", instance.ProtectedParameters), ("@hosting", instance.HostingProviderId),
                ("@time", instance.CreatedAt.ToString("O", CultureInfo.InvariantCulture)));
            command.ExecuteNonQuery();
            foreach (string tag in instance.TagIds)
            {
                Set(command, "INSERT INTO instance_tag (instance_id, tag_id) VALUES (@instance, @tag);", ("@instance", instance.Id), ("@tag", tag));
                command.ExecuteNonQuery();
            }
        }
        SqliteAgentModelStore.WriteProtected(command, protectedModels);
        cancellationToken.ThrowIfCancellationRequested();
        if (owner is not null)
        {
            Set(command, """
                INSERT INTO app_setting (id, value, update_time) VALUES ('sync.activeOwner', @owner, @time)
                ON CONFLICT(id) DO UPDATE SET value = excluded.value, update_time = excluded.update_time;
                """, ("@owner", owner), ("@time", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            command.ExecuteNonQuery();
        }
        transaction.Commit();
    }
    /// <summary>参数化写入，SQL 片段全部由本模块固定。</summary>
    private static void Set(DbCommand command, string sql, params (string Name, object? Value)[] values)
    {
        command.CommandText = sql; command.Parameters.Clear();
        foreach (var value in values)
        {
            var parameter = command.CreateParameter(); parameter.ParameterName = value.Name; parameter.Value = value.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
