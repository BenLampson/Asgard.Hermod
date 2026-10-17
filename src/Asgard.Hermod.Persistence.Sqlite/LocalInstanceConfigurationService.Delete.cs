namespace Asgard.Hermod.Persistence.Sqlite;

public sealed partial class LocalInstanceConfigurationService
{
    /// <inheritdoc />
    public Task DeleteAsync(string instanceId, InstanceDeleteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => { _repository.DeleteInstance(instanceId, cancellationToken); _addresses.TryRemove(instanceId, out _); }, cancellationToken);
    }
}
