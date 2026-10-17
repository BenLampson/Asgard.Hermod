namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqForm
{
    private void BuildFields()
    {
        if (IsConfirmation)
        {
            if (Operation == RabbitMqOperation.DeleteQueue) Field("onlyEmpty", "RabbitMqOnlyEmpty", "true", "boolean");
            if (Operation is RabbitMqOperation.DeleteQueue or RabbitMqOperation.DeleteExchange) Field("onlyUnused", "RabbitMqOnlyUnused", "true", "boolean");
            if (Operation == RabbitMqOperation.CloseConnection) Field("reason", "RabbitMqCloseReason", Text.Get("RabbitMqCloseReasonDefault"));
            return;
        }
        string Existing(string key, string fallback = "")
        {
            var value = _existing?.Value(key) ?? default;
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? fallback,
                JsonValueKind.Array => string.Join(',', value.EnumerateArray().Select(item => item.GetString() ?? "")),
                JsonValueKind.Number => value.GetRawText(),
                _ => fallback,
            };
        }
        bool editing = _existing is not null;
        if (Operation != RabbitMqOperation.Publish)
            Field("name", Operation is RabbitMqOperation.PutPermissions or RabbitMqOperation.PutTopicPermissions or RabbitMqOperation.PutUser ? "RabbitMqUser" : Operation == RabbitMqOperation.CreateBinding ? "RabbitMqSource" : "RabbitMqName",
                Name, required: true, readOnly: editing && Operation != RabbitMqOperation.CreateBinding);
        switch (Operation)
        {
            case RabbitMqOperation.DeclareQueue:
                Choice("type", "RabbitMqType", "classic", "classic", "quorum", "stream");
                Field("durable", "RabbitMqDurable", "true", "boolean"); Field("auto_delete", "RabbitMqAutoDelete", "false", "boolean"); break;
            case RabbitMqOperation.DeclareExchange:
                Choice("type", "RabbitMqType", "direct", "direct", "fanout", "topic", "headers");
                Field("durable", "RabbitMqDurable", "true", "boolean"); Field("auto_delete", "RabbitMqAutoDelete", "false", "boolean"); Field("internal", "RabbitMqInternal", "false", "boolean"); break;
            case RabbitMqOperation.CreateBinding:
                Choice("destinationType", "RabbitMqDestinationType", "queue", "queue", "exchange");
                Field("destination", "RabbitMqDestination", required: true); Field("routing_key", "RabbitMqRoutingKey"); break;
            case RabbitMqOperation.PutVhost:
                Field("description", "RabbitMqDescription", Existing("description"));
                Choice("default_queue_type", "RabbitMqDefaultQueueType", Existing("default_queue_type", "classic"), "classic", "quorum", "stream");
                Field("tags", "RabbitMqTags", Existing("tags")); break;
            case RabbitMqOperation.PutUser:
                Field("password", "RabbitMqPassword", kind: "secret", required: !editing); Field("repeat", "RabbitMqPasswordRepeat", kind: "secret", required: !editing);
                foreach (string role in new[] { "management", "policymaker", "monitoring", "administrator" })
                    Field(role, "RabbitMqRole" + role, Existing("tags").Contains(role, StringComparison.Ordinal) ? "true" : "false", "boolean");
                break;
            case RabbitMqOperation.PutPermissions:
                Field("configure", "RabbitMqConfigurePattern", Existing("configure")); Field("write", "RabbitMqWritePattern", Existing("write")); Field("read", "RabbitMqReadPattern", Existing("read")); break;
            case RabbitMqOperation.PutTopicPermissions:
                Field("exchange", "RabbitMqExchange", Existing("exchange"), required: true);
                Field("write", "RabbitMqWritePattern", Existing("write")); Field("read", "RabbitMqReadPattern", Existing("read")); break;
            case RabbitMqOperation.PutPolicy:
                Field("pattern", "RabbitMqPattern", Existing("pattern"), required: true);
                Choice("apply-to", "RabbitMqApplyTo", Existing("apply-to", "queues"), "all", "queues", "exchanges", "classic_queues", "quorum_queues", "streams");
                Field("priority", "RabbitMqPriority", Existing("priority", "0"), required: true);
                foreach (var argument in RabbitMqArgument.From(_existing?.Value("definition") ?? default)) Arguments.Add(argument); break;
            case RabbitMqOperation.Publish:
                Field("exchange", "RabbitMqExchange", Name, readOnly: true); Field("routing_key", "RabbitMqRoutingKey");
                Choice("payload_encoding", "RabbitMqEncoding", "string", "string", "base64");
                Field("content_type", "RabbitMqContentType", "application/json"); Choice("delivery_mode", "RabbitMqDeliveryMode", "2", "1", "2");
                Field("payload", "RabbitMqPayload", "", "multiline");
                foreach (string key in new[] { "message_id", "correlation_id", "reply_to", "expiration" }) Field(key, "RabbitMqProperty" + key);
                break;
        }
    }
    /// <summary>从草稿构造固定操作，不允许 arbitrary URL 或直接 HTTP 片段。</summary>
    public RabbitMqMutation BuildMutation()
    {
        if (Fields.Any(item => item.Required && item.Value.Length == 0)) throw Text.Exception("RabbitMqRequired");
        var body = new Dictionary<string, object?>(StringComparer.Ordinal); string name = IsConfirmation || Operation == RabbitMqOperation.Publish ? Name : Value("name");
        if (IsConfirmation)
        {
            if (Confirmation != Name) throw Text.Exception("RabbitMqConfirmationMismatch");
            if (Operation == RabbitMqOperation.CloseConnection) body["reason"] = Value("reason");
        }
        else switch (Operation)
            {
                case RabbitMqOperation.DeclareQueue:
                case RabbitMqOperation.DeclareExchange:
                    var arguments = RabbitMqArgument.Build(Arguments);
                    if (Operation == RabbitMqOperation.DeclareQueue)
                    {
                        if (arguments.ContainsKey("x-queue-type")) throw Text.Exception("RabbitMqQueueTypeArgument");
                        arguments["x-queue-type"] = Value("type");
                    }
                    else { body["type"] = Value("type"); body["internal"] = Flag("internal"); }
                    body["durable"] = Flag("durable"); body["auto_delete"] = Flag("auto_delete"); body["arguments"] = arguments; break;
                case RabbitMqOperation.CreateBinding:
                    body["routing_key"] = Value("routing_key"); body["arguments"] = RabbitMqArgument.Build(Arguments); break;
                case RabbitMqOperation.PutVhost:
                    body["description"] = Value("description"); body["default_queue_type"] = Value("default_queue_type");
                    body["tags"] = Value("tags").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case RabbitMqOperation.PutUser:
                    if (Value("password") != Value("repeat")) throw Text.Exception("RabbitMqPasswordMismatch");
                    if (Value("password").Length > 0) body["password"] = Value("password");
                    body["tags"] = string.Join(',', new[] { "management", "policymaker", "monitoring", "administrator" }.Where(Flag)); break;
                case RabbitMqOperation.PutPermissions:
                    body["configure"] = Value("configure"); body["write"] = Value("write"); body["read"] = Value("read"); break;
                case RabbitMqOperation.PutTopicPermissions:
                    body["exchange"] = Value("exchange"); body["write"] = Value("write"); body["read"] = Value("read"); break;
                case RabbitMqOperation.PutPolicy:
                    if (!int.TryParse(Value("priority"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int priority)) throw Text.Exception("RabbitMqPriorityInvalid");
                    body["pattern"] = Value("pattern"); body["apply-to"] = Value("apply-to"); body["priority"] = priority; body["definition"] = RabbitMqArgument.Build(Arguments);
                    if (((Dictionary<string, object?>)body["definition"]!).Count == 0) throw Text.Exception("RabbitMqDefinitionRequired"); break;
                case RabbitMqOperation.Publish:
                    body["routing_key"] = Value("routing_key"); body["payload_encoding"] = Value("payload_encoding"); body["payload"] = Value("payload");
                    var properties = new Dictionary<string, object?> { ["content_type"] = Value("content_type"), ["delivery_mode"] = int.Parse(Value("delivery_mode"), CultureInfo.InvariantCulture), ["headers"] = RabbitMqArgument.Build(Arguments) };
                    foreach (string key in new[] { "message_id", "correlation_id", "reply_to", "expiration" }) if (Value(key).Length > 0) properties[key] = Value(key);
                    body["properties"] = properties; break;
            }
        var result = new RabbitMqMutation(Operation, Vhost, name, JsonSerializer.SerializeToElement(body), Value("destination"), Value("destinationType"),
            _existing?.String("properties_key") ?? "", IsConfirmation ? Flag("onlyEmpty") : true, IsConfirmation ? Flag("onlyUnused") : true);
        if (Operation == RabbitMqOperation.DeleteBinding && _existing is not null)
            result = result with { Destination = _existing.String("destination"), DestinationType = _existing.String("destination_type") };
        return result;
    }
}
