namespace Asgard.Hermod.Persistence.Sqlite;

public sealed partial class SqliteConfigurationRepository
{
    /// <inheritdoc />
    public void DeleteInstance(string instanceId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);
        InTransaction(true, command =>
        {
            SetCommand(command, "DELETE FROM instance_tag WHERE instance_id = @id;", ("@id", instanceId));
            command.ExecuteNonQuery();
            SetCommand(command, "DELETE FROM instance_configuration WHERE id = @id;", ("@id", instanceId));
            if (command.ExecuteNonQuery() != 1) throw Text.Exception("TheInstanceNoLongerExistsRefreshAndConfigureItAgain");
            return true;
        }, cancellationToken);
    }
}
