namespace Asgard.Hermod.RabbitMq;

/// <summary>发送之前验证协议约束；正则表达式由 Erlang 服务端解释，不用 .NET 规则误判。</summary>
public static class RabbitMqValidation
{
    /// <summary>所有写操作均通过同一校验入口。</summary>
    public static void Validate(RabbitMqMutation mutation)
    {
        if (mutation.Name.Any(char.IsControl) || Encoding.UTF8.GetByteCount(mutation.Name) > 255
            || mutation.Vhost.Any(char.IsControl)) throw Text.Exception("RabbitMqNameInvalid");
        if (mutation.Name.Length == 0 && mutation.Operation != RabbitMqOperation.Publish) throw Text.Exception("RabbitMqNameInvalid");
        if (mutation.Operation is RabbitMqOperation.DeclareQueue or RabbitMqOperation.DeclareExchange && mutation.Name.StartsWith("amq.", StringComparison.Ordinal))
            throw Text.Exception("RabbitMqReservedName");
        if (mutation.Operation is RabbitMqOperation.DeleteExchange && mutation.Name.StartsWith("amq.", StringComparison.Ordinal))
            throw Text.Exception("RabbitMqSystemExchange");
        if (mutation.Body.ValueKind == JsonValueKind.Undefined) return;
        if (mutation.Body.ValueKind != JsonValueKind.Object || Encoding.UTF8.GetByteCount(mutation.Body.GetRawText()) > 8 * 1024 * 1024)
            throw Text.Exception("RabbitMqBodyInvalid");
        var data = new RabbitMqRecord(mutation.Body);
        if (mutation.Operation == RabbitMqOperation.DeclareQueue)
        {
            string type = data.String("arguments.x-queue-type");
            if (type is not ("classic" or "quorum" or "stream") ||
                type is "quorum" or "stream" && (!data.Boolean("durable") || data.Boolean("auto_delete")))
                throw Text.Exception("RabbitMqQueueTypeInvalid");
        }
        if (mutation.Operation == RabbitMqOperation.DeclareExchange && data.String("type") is not ("direct" or "fanout" or "topic" or "headers"))
            throw Text.Exception("RabbitMqExchangeTypeInvalid");
        if (mutation.Operation == RabbitMqOperation.CreateBinding && mutation.DestinationType is not ("queue" or "exchange"))
            throw Text.Exception("RabbitMqBodyInvalid");
        if (mutation.Operation == RabbitMqOperation.CloseConnection && (data.String("reason").Any(char.IsControl) || data.String("reason").Length > 256))
            throw Text.Exception("RabbitMqReasonInvalid");
        if (mutation.Operation == RabbitMqOperation.Publish)
        {
            string payload = data.String("payload"), encoding = data.String("payload_encoding"); byte[] bytes;
            try { bytes = encoding == "base64" ? Convert.FromBase64String(payload) : Encoding.UTF8.GetBytes(payload); }
            catch (FormatException) { throw Text.Exception("RabbitMqBase64Invalid"); }
            if (encoding is not ("base64" or "string") || bytes.Length > 1024 * 1024) throw Text.Exception("RabbitMqPayloadTooLarge");
        }
    }
}
