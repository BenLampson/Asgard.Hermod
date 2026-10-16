namespace Asgard.Hermod.RabbitMq;

public sealed partial class RabbitMqSession
{
    /// <inheritdoc />
    public async Task<JsonElement> ExecuteAsync(RabbitMqMutation mutation, CancellationToken cancellationToken = default)
    {
        RabbitMqValidation.Validate(mutation);
        string host = Segment(mutation.Vhost), name = Segment(mutation.Name), exchange = Exchange(mutation.Name);
        string binding = $"bindings/{host}/e/{exchange}/{(mutation.DestinationType == "exchange" ? "e" : "q")}/{Segment(mutation.Destination)}";
        (HttpMethod method, string path) = mutation.Operation switch
        {
            RabbitMqOperation.DeclareQueue => (HttpMethod.Put, $"queues/{host}/{name}"),
            RabbitMqOperation.DeclareExchange => (HttpMethod.Put, $"exchanges/{host}/{exchange}"),
            RabbitMqOperation.CreateBinding => (HttpMethod.Post, binding),
            RabbitMqOperation.DeleteQueue => (HttpMethod.Delete, $"queues/{host}/{name}?if-empty={Bool(mutation.OnlyEmpty)}&if-unused={Bool(mutation.OnlyUnused)}"),
            RabbitMqOperation.DeleteExchange => (HttpMethod.Delete, $"exchanges/{host}/{exchange}?if-unused={Bool(mutation.OnlyUnused)}"),
            RabbitMqOperation.DeleteBinding => (HttpMethod.Delete, binding + "/" + Segment(mutation.PropertiesKey)),
            RabbitMqOperation.PurgeQueue => (HttpMethod.Delete, $"queues/{host}/{name}/contents"),
            RabbitMqOperation.CloseConnection => (HttpMethod.Delete, $"connections/{name}"),
            RabbitMqOperation.PutVhost => (HttpMethod.Put, $"vhosts/{name}"),
            RabbitMqOperation.DeleteVhost => (HttpMethod.Delete, $"vhosts/{name}"),
            RabbitMqOperation.PutUser => (HttpMethod.Put, $"users/{name}"),
            RabbitMqOperation.DeleteUser => (HttpMethod.Delete, $"users/{name}"),
            RabbitMqOperation.PutPermissions => (HttpMethod.Put, $"permissions/{host}/{name}"),
            RabbitMqOperation.DeletePermissions => (HttpMethod.Delete, $"permissions/{host}/{name}"),
            RabbitMqOperation.PutTopicPermissions => (HttpMethod.Put, $"topic-permissions/{host}/{name}"),
            RabbitMqOperation.DeleteTopicPermissions => (HttpMethod.Delete, $"topic-permissions/{host}/{name}"),
            RabbitMqOperation.PutPolicy => (HttpMethod.Put, $"policies/{host}/{name}"),
            RabbitMqOperation.DeletePolicy => (HttpMethod.Delete, $"policies/{host}/{name}"),
            RabbitMqOperation.Publish => (HttpMethod.Put, $"exchanges/{host}/{exchange}/publish"),
            _ => throw Text.Exception("RabbitMqConfigurationInvalid"),
        };
        if (mutation.Operation == RabbitMqOperation.PurgeQueue)
        {
            var queue = await ReadObjectAsync(RabbitMqResource.Queues, mutation.Vhost, mutation.Name, cancellationToken).ConfigureAwait(false);
            if (queue.String("type") == "stream") throw Text.Exception("RabbitMqStreamUnsupported");
        }
        var result = await RequestAsync(method, path, method == HttpMethod.Delete ? default : mutation.Body, true, cancellationToken,
            mutation.Operation == RabbitMqOperation.CloseConnection ? new RabbitMqRecord(mutation.Body).String("reason") : null).ConfigureAwait(false);
        if (mutation.Operation == RabbitMqOperation.Publish && (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("routed", out var routed) || routed.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            throw new RabbitMqFailure("RabbitMqOutcomeUnknown", true);
        return result;
    }
    private static string Bool(bool value) => value ? "true" : "false";
}
