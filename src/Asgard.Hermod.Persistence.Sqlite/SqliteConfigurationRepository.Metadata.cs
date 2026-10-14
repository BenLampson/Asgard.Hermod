namespace Asgard.Hermod.Persistence.Sqlite;

public sealed partial class SqliteConfigurationRepository
{
    /// <inheritdoc />
    public LocalConfigurationSnapshot UpdateInstanceMetadata(string instanceId, InstanceMetadataChange change,
        CancellationToken cancellationToken = default) => InTransaction(true, command =>
    {
        var snapshot = ReadSnapshot(command, cancellationToken);
        if (!snapshot.Instances.Any(instance => instance.Id == instanceId))
            throw Text.Exception("TheInstanceNoLongerExistsRefreshAndConfigureItAgain");
        if (change.Name is { } name)
        {
            SetCommand(command, "UPDATE instance_configuration SET name = @name WHERE id = @id;", ("@name", name), ("@id", instanceId));
            command.ExecuteNonQuery();
        }
        if (change.TagIds is { } ids)
        {
            var requested = ids.Distinct(StringComparer.Ordinal).ToArray();
            var known = snapshot.Tags.Select(tag => tag.Id).ToHashSet(StringComparer.Ordinal);
            if (requested.Any(id => !known.Contains(id)))
                throw Text.Exception("TheSelectedTagsNoLongerExistRefreshAndSelectAgain");
            SetCommand(command, "DELETE FROM instance_tag WHERE instance_id = @id;", ("@id", instanceId));
            command.ExecuteNonQuery();
            foreach (string id in requested)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SetCommand(command, "INSERT INTO instance_tag (instance_id, tag_id) VALUES (@instance, @tag);", ("@instance", instanceId), ("@tag", id));
                command.ExecuteNonQuery();
            }
        }
        // 在提交前读取结果；提交后不再读取，避免已提交的操作误报失败。
        return ReadSnapshot(command, cancellationToken);
    }, cancellationToken);

    /// <inheritdoc />
    public ManagedTag RenameTag(string tagId, string name, CancellationToken cancellationToken = default) => InTransaction(true, command =>
    {
        var tags = ReadTags(command);
        var original = tags.SingleOrDefault(tag => tag.Id == tagId) ?? throw Text.Exception("TheTagDoesNotExistRefreshAndTryAgain");
        var validated = TagHierarchy.ValidateEdit(tagId, new(name, original.ParentId, original.Color), tags);
        SetCommand(command, "UPDATE managed_tag SET name = @name, name_key = @key WHERE id = @id AND is_built_in = 0;",
            ("@name", validated.Name), ("@key", TagHierarchy.NameKey(validated.Name)), ("@id", tagId));
        if (command.ExecuteNonQuery() != 1) throw Text.Exception("TheTagDoesNotExistRefreshAndTryAgain");
        return TagHierarchy.ToSnapshots(ReadTags(command), ReadAssociations(command)).Single(tag => tag.Id == tagId);
    }, cancellationToken);
}
