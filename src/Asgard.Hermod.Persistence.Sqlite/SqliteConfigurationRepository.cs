namespace Asgard.Hermod.Persistence.Sqlite;

/// <summary>复用应用 FreeSql 连接池，以参数化 SQLite 命令完成密文配置和标签事务。</summary>
public sealed partial class SqliteConfigurationRepository : ILocalConfigurationRepository
{
    /// <summary>由应用容器释放的唯一业务数据库连接池。</summary>
    private readonly IFreeSql _database;

    /// <summary>仓储只依赖数据库，不读取明文凭据，也不依赖窗口。</summary>
    public SqliteConfigurationRepository(IFreeSql database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _database = database;
    }

    /// <inheritdoc />
    public LocalConfigurationSnapshot GetSnapshot(CancellationToken cancellationToken = default) =>
        InTransaction(false, command => ReadSnapshot(command, cancellationToken), cancellationToken);

    /// <inheritdoc />
    public StoredInstanceConfiguration? GetInstance(string instanceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        return InTransaction(false, command =>
        {
            SetCommand(command, """
                SELECT id, name, type_id, provider_id, protected_parameters, create_time
                FROM instance_configuration WHERE id = @id;
                """, ("@id", instanceId));
            StoredInstanceConfiguration? instance;
            using (var reader = command.ExecuteReader())
                instance = reader.Read() ? ReadInstance(reader, []) : null;
            if (instance is null) return null;
            var associations = ReadAssociations(command);
            return instance with { TagIds = associations.GetValueOrDefault(instanceId) ?? [] };
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void CreateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        InTransaction(true, command =>
        {
            var knownTags = ReadTags(command).Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
            string[] tagIds = instance.TagIds.Distinct(StringComparer.Ordinal).ToArray();
            if (tagIds.Any(id => !knownTags.Contains(id)))
                throw Text.Exception("TheSelectedTagsNoLongerExistRefreshAndSelectAgain");
            SetCommand(command, """
                INSERT INTO instance_configuration (id, name, type_id, provider_id, protected_parameters, create_time)
                VALUES (@id, @name, @type, @provider, @parameters, @created);
                """, ("@id", instance.Id), ("@name", instance.Name), ("@type", instance.TypeId), ("@provider", instance.ProviderId),
                ("@parameters", instance.ProtectedParameters),
                ("@created", instance.CreatedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)));
            command.ExecuteNonQuery();
            foreach (string tagId in tagIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetCommand(command, "INSERT INTO instance_tag (instance_id, tag_id) VALUES (@instance, @tag);",
                    ("@instance", instance.Id), ("@tag", tagId));
                command.ExecuteNonQuery();
            }
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public void UpdateInstance(StoredInstanceConfiguration instance, CancellationToken cancellationToken = default) =>
        UpdateInstance(instance, false, cancellationToken);

    /// <inheritdoc />
    public void UpdateInstance(StoredInstanceConfiguration instance, bool replaceTags, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);
        InTransaction(true, command =>
        {
            // 配置与标签在同一事务中验证和替换，目录并发删除不能留下半次更新。
            var knownTags = ReadTags(command).Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
            string[] tagIds = instance.TagIds.Distinct(StringComparer.Ordinal).ToArray();
            if (replaceTags && tagIds.Any(id => !knownTags.Contains(id)))
                throw Text.Exception("TheSelectedTagsNoLongerExistRefreshAndSelectAgain");
            SetCommand(command, """
                UPDATE instance_configuration
                SET name = @name, type_id = @type, provider_id = @provider, protected_parameters = @parameters
                WHERE id = @id;
                """, ("@id", instance.Id), ("@name", instance.Name), ("@type", instance.TypeId),
                ("@provider", instance.ProviderId), ("@parameters", instance.ProtectedParameters));
            if (command.ExecuteNonQuery() != 1)
                throw Text.Exception("TheInstanceNoLongerExistsRefreshAndConfigureItAgain");
            if (!replaceTags) return true;
            SetCommand(command, "DELETE FROM instance_tag WHERE instance_id = @instance;", ("@instance", instance.Id));
            command.ExecuteNonQuery();
            foreach (string tagId in tagIds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetCommand(command, "INSERT INTO instance_tag (instance_id, tag_id) VALUES (@instance, @tag);",
                    ("@instance", instance.Id), ("@tag", tagId));
                command.ExecuteNonQuery();
            }
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public ManagedTag SaveTag(string? tagId, TagEditDraft draft, CancellationToken cancellationToken = default) =>
        InTransaction(true, command =>
        {
            // 检查与写入持有同一写锁，多个管理窗口不能穿过同级唯一和层级规则。
            var existing = ReadTags(command);
            TagEditDraft validated = TagHierarchy.ValidateEdit(tagId, draft, existing);
            string id = tagId ?? Guid.NewGuid().ToString("N");
            SetCommand(command, tagId is null
                ? """
                  INSERT INTO managed_tag (id, name, name_key, parent_id, color, is_built_in)
                  VALUES (@id, @name, @key, @parent, @color, 0);
                  """
                : """
                  UPDATE managed_tag SET name = @name, name_key = @key, parent_id = @parent, color = @color
                  WHERE id = @id AND is_built_in = 0;
                  """, ("@id", id), ("@name", validated.Name), ("@key", TagHierarchy.NameKey(validated.Name)),
                ("@parent", validated.ParentId), ("@color", validated.Color));
            command.ExecuteNonQuery();
            return TagHierarchy.ToSnapshots(ReadTags(command), ReadAssociations(command)).Single(tag => tag.Id == id);
        }, cancellationToken);

    /// <inheritdoc />
    public TagDeletionPreview PreviewTagDeletion(string tagId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        return InTransaction(false, command => BuildDeletionPreview(command, tagId), cancellationToken);
    }

    /// <inheritdoc />
    public void DeleteTag(string tagId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagId);
        InTransaction(true, command =>
        {
            TagDeletionPreview preview = BuildDeletionPreview(command, tagId);
            // 先删关联，再按最深路径向上删标签，即使启用了外键约束也不会留下悬挂引用。
            foreach (ManagedTag tag in preview.Tags.OrderByDescending(tag => tag.Path.Count(value => value == '/')))
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetCommand(command, "DELETE FROM instance_tag WHERE tag_id = @tag;", ("@tag", tag.Id));
                command.ExecuteNonQuery();
                SetCommand(command, "DELETE FROM managed_tag WHERE id = @tag AND is_built_in = 0;", ("@tag", tag.Id));
                command.ExecuteNonQuery();
            }
            return true;
        }, cancellationToken);
    }

    /// <summary>预览读取完整子树，并按实例标识计算影响，重复关联不重复计数。</summary>
    private static TagDeletionPreview BuildDeletionPreview(DbCommand command, string tagId)
    {
        var tags = ReadTags(command);
        TagHierarchy.EnsureEditable(tagId, tags);
        HashSet<string> subtree = TagHierarchy.GetSubtree(tagId, tags);
        var associations = ReadAssociations(command);
        return new TagDeletionPreview(tagId,
            TagHierarchy.ToSnapshots(tags, associations).Where(tag => subtree.Contains(tag.Id)).ToArray(),
            associations.Count(pair => pair.Value.Any(subtree.Contains)));
    }

    /// <summary>实例、标签路径和计数由同一个读取事务提供，不会出现跨快照的关联。</summary>
    internal static LocalConfigurationSnapshot ReadSnapshot(DbCommand command, CancellationToken cancellationToken)
    {
        var tags = ReadTags(command);
        var associations = ReadAssociations(command);
        SetCommand(command, """
            SELECT id, name, type_id, provider_id, protected_parameters, create_time
            FROM instance_configuration ORDER BY create_time, id;
            """);
        var instances = new List<StoredInstanceConfiguration>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cancellationToken.ThrowIfCancellationRequested();
            instances.Add(ReadInstance(reader, associations.GetValueOrDefault(reader.GetString(0)) ?? []));
        }
        return new LocalConfigurationSnapshot(instances, TagHierarchy.ToSnapshots(tags, associations));
    }

    /// <summary>将密文列映射到内部记录，任何导航读取都不会触发解密。</summary>
    private static StoredInstanceConfiguration ReadInstance(DbDataReader reader, IReadOnlyList<string> tagIds) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), (byte[])reader.GetValue(4),
        DateTime.ParseExact(reader.GetString(5), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), tagIds);

    /// <summary>只读取标签基础字段，显示路径和关联计数在内存中统一生成。</summary>
    private static IReadOnlyList<StoredTag> ReadTags(DbCommand command)
    {
        SetCommand(command, "SELECT id, name, parent_id, color, is_built_in FROM managed_tag;");
        var result = new List<StoredTag>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
            result.Add(new StoredTag(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3), reader.GetInt32(4) != 0));
        return result;
    }

    /// <summary>按稳定实例标识聚合标签标识，不依赖易变化的显示名称。</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadAssociations(DbCommand command)
    {
        SetCommand(command, "SELECT instance_id, tag_id FROM instance_tag ORDER BY instance_id, tag_id;");
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            string id = reader.GetString(0);
            if (!result.TryGetValue(id, out var tags)) result[id] = tags = [];
            tags.Add(reader.GetString(1));
        }
        return result.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    /// <summary>连接池租约、外键设置、事务和取消边界全部集中在仓储，不暴露给调用方。</summary>
    private T InTransaction<T>(bool write, Func<DbCommand, T> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var lease = _database.Ado.MasterPool.Get();
        var connection = lease.Value;
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        // 写入立即加锁；读取使用延迟事务，WAL 模式下允许其他读取并行进行。
        using var transaction = connection.BeginTransaction(write ? IsolationLevel.Serializable : IsolationLevel.ReadCommitted);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        cancellationToken.ThrowIfCancellationRequested();
        T result = action(command);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return result;
    }

    /// <summary>所有外部字段均通过数据库参数传入，动态 SQL 只来自程序中的固定常量。</summary>
    private static void SetCommand(DbCommand command, string sql, params (string Name, object? Value)[] values)
    {
        command.CommandText = sql;
        command.Parameters.Clear();
        foreach (var value in values)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = value.Name;
            parameter.Value = value.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }
    }
}
