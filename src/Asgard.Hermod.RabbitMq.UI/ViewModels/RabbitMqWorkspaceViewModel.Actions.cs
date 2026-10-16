namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    public bool CanCreate => CanRead && (SelectedVhost is not null && Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges or RabbitMqResource.Bindings
        || _session!.IsAdministrator && Resource is RabbitMqResource.Vhosts or RabbitMqResource.Users
        || ScopeVhost is not null && _session!.IsAdministrator && Resource is RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions
        || ScopeVhost is not null && _session!.CanManagePolicies && Resource == RabbitMqResource.Policies);
    public bool CanEdit => CanCreate && Target is not null && Resource is RabbitMqResource.Users or RabbitMqResource.Vhosts or RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions or RabbitMqResource.Policies;
    public bool CanDelete => CanRead && Target is { } target && Resource switch
    {
        RabbitMqResource.Queues => true,
        RabbitMqResource.Exchanges => target.Name.Length > 0 && !target.Name.StartsWith("amq.", StringComparison.Ordinal),
        RabbitMqResource.Bindings => target.String("source").Length > 0 && target.String("properties_key").Length > 0,
        RabbitMqResource.Connections => true,
        RabbitMqResource.Vhosts or RabbitMqResource.Users or RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions => _session!.IsAdministrator,
        RabbitMqResource.Policies => _session!.CanManagePolicies,
        _ => false,
    };
    public bool HasQueueActions => Resource == RabbitMqResource.Queues;
    public bool HasPublish => Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges;
    public string CreateLabel => Text.Get("RabbitMq" + CreateOperation);
    public string DeleteLabel => Text.Get(Resource == RabbitMqResource.Connections ? "RabbitMqCloseConnection" : "RabbitMqDelete");
    private RabbitMqOperation CreateOperation => Resource switch
    {
        RabbitMqResource.Queues => RabbitMqOperation.DeclareQueue,
        RabbitMqResource.Exchanges => RabbitMqOperation.DeclareExchange,
        RabbitMqResource.Bindings => RabbitMqOperation.CreateBinding,
        RabbitMqResource.Vhosts => RabbitMqOperation.PutVhost,
        RabbitMqResource.Users => RabbitMqOperation.PutUser,
        RabbitMqResource.Permissions => RabbitMqOperation.PutPermissions,
        RabbitMqResource.TopicPermissions => RabbitMqOperation.PutTopicPermissions,
        _ => RabbitMqOperation.PutPolicy,
    };
    private string WriteTarget => Text.Format("RabbitMqWriteTarget", Instance.Name, Instance.EnvironmentLabel, Endpoint, ScopeVhost ?? Text.Get("RabbitMqGlobalScope"));
    private async Task EditResourceAsync(bool editing)
    {
        var record = editing ? Target : null;
        string name = record?.Name ?? "";
        if (editing && Resource is RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions) name = record!.String("user");
        var form = new RabbitMqForm(CreateOperation, ScopeVhost ?? "", name, WriteTarget, ExecuteMutationAsync, record);
        await ShowFormAsync(form);
    }
    private async Task PublishAsync()
    {
        if (Target is not { } target) return;
        string exchange = Resource == RabbitMqResource.Queues ? "" : target.Name;
        var form = new RabbitMqForm(RabbitMqOperation.Publish, SelectedVhost!, exchange, WriteTarget, async mutation =>
        {
            try
            {
                var result = await _session!.ExecuteAsync(mutation, _lifetime.Token);
                Notice = Text.Get(result.ValueKind == JsonValueKind.Object && result.TryGetProperty("routed", out var routed) && routed.ValueKind == JsonValueKind.True ? "RabbitMqPublishRouted" : "RabbitMqPublishUnrouted");
            }
            catch (Exception error) { Fail(error); throw; }
        });
        if (Resource == RabbitMqResource.Queues) form.Fields.First(item => item.Key == "routing_key").Value = target.Name;
        await ShowFormAsync(form);
    }
    private async Task DeleteAsync(bool purge)
    {
        if (Target is not { } target) return;
        var operation = purge ? RabbitMqOperation.PurgeQueue : Resource switch
        {
            RabbitMqResource.Queues => RabbitMqOperation.DeleteQueue,
            RabbitMqResource.Exchanges => RabbitMqOperation.DeleteExchange,
            RabbitMqResource.Bindings => RabbitMqOperation.DeleteBinding,
            RabbitMqResource.Connections => RabbitMqOperation.CloseConnection,
            RabbitMqResource.Vhosts => RabbitMqOperation.DeleteVhost,
            RabbitMqResource.Users => RabbitMqOperation.DeleteUser,
            RabbitMqResource.Permissions => RabbitMqOperation.DeletePermissions,
            RabbitMqResource.TopicPermissions => RabbitMqOperation.DeleteTopicPermissions,
            _ => RabbitMqOperation.DeletePolicy,
        };
        string name = Resource is RabbitMqResource.Permissions or RabbitMqResource.TopicPermissions ? target.String("user")
            : Resource == RabbitMqResource.Bindings ? target.String("source") : target.Name;
        await ShowFormAsync(new(operation, ScopeVhost ?? "", name, WriteTarget, async mutation =>
        {
            await ExecuteMutationAsync(mutation);
            if (!purge) { _inDetail = _inMessages = false; _detail = null; }
        }, target, true));
    }
    private async Task ExecuteMutationAsync(RabbitMqMutation mutation)
    {
        try { await _session!.ExecuteAsync(mutation, _lifetime.Token); Notice = Text.Get("RabbitMqOperationSucceeded"); }
        catch (Exception error) { Fail(error); throw; }
    }
    private async Task ShowFormAsync(RabbitMqForm form)
    {
        _read?.Cancel(); IsDialogOpen = true; RefreshState();
        bool success = false;
        try { success = await _dialogs.ShowAsync(form); }
        finally { IsDialogOpen = false; RefreshState(); }
        if (success)
        {
            if (form.Operation is RabbitMqOperation.PutVhost or RabbitMqOperation.DeleteVhost or RabbitMqOperation.PutPermissions or RabbitMqOperation.DeletePermissions)
                await RefreshScopesAsync(_session!, _lifetime.Token);
            await LoadAsync();
        }
    }
}
