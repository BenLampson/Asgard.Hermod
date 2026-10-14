namespace Asgard.Hermod.Persistence.Sqlite;

public sealed partial class LocalInstanceConfigurationService
{
    /// <inheritdoc />
    public Task<InstanceSummary> UpdateMetadataAsync(string instanceId, InstanceMetadataChange change,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        ArgumentNullException.ThrowIfNull(change);
        string? name = change.Name?.Trim();
        if (name is not null && (name.Length == 0 || name.Length > 128 || name.Any(char.IsControl)))
            throw Text.Exception("InstanceNamesMustContain1128CharactersAndNoControlCharacters");
        var normalized = change with { Name = name, TagIds = change.TagIds?.ToArray() };
        return Task.Run(() =>
        {
            var descriptors = _providers.Types.SelectMany(type => _providers.GetProviders(type.Id)).ToArray();
            var snapshot = _repository.UpdateInstanceMetadata(instanceId, normalized, cancellationToken);
            var saved = snapshot.Instances.Single(instance => instance.Id == instanceId);
            return CreateSummary(saved, snapshot.Tags, descriptors.FirstOrDefault(provider => provider.Type.Id == saved.TypeId && provider.Id == saved.ProviderId))
                with
            { ConnectionAddress = _addresses.GetValueOrDefault(saved.Id).Address ?? "" };
        }, cancellationToken);
    }
}
