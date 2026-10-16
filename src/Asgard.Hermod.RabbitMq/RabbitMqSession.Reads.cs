namespace Asgard.Hermod.RabbitMq;

public sealed partial class RabbitMqSession
{
    /// <inheritdoc />
    public async Task<RabbitMqPage> ReadPageAsync(RabbitMqResource resource, string vhost = "", string search = "", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100); string host = Segment(vhost);
        string path = resource switch
        {
            RabbitMqResource.Queues => $"queues/{host}",
            RabbitMqResource.Exchanges => $"exchanges/{host}",
            RabbitMqResource.Bindings => $"bindings/{host}",
            RabbitMqResource.Connections => $"vhosts/{host}/connections",
            RabbitMqResource.Channels => $"vhosts/{host}/channels",
            RabbitMqResource.Consumers => $"consumers/{host}",
            RabbitMqResource.Vhosts => "vhosts",
            RabbitMqResource.Users => "users",
            RabbitMqResource.OwnPermissions => $"users/{Segment(Identity.String("name"))}/permissions",
            RabbitMqResource.Permissions => $"vhosts/{host}/permissions",
            RabbitMqResource.TopicPermissions => $"vhosts/{host}/topic-permissions",
            RabbitMqResource.Policies => $"policies/{host}",
            RabbitMqResource.Nodes => "nodes",
            _ => throw Text.Exception("RabbitMqConfigurationInvalid"),
        };
        // 仅使用服务端声明支持分页的端点，其余列表在有界响应上明确本地分页。
        bool paged = resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges or RabbitMqResource.Vhosts or RabbitMqResource.Users or RabbitMqResource.Connections or RabbitMqResource.Channels;
        if (paged) path += $"?pagination=true&page={page.ToString(CultureInfo.InvariantCulture)}&page_size={pageSize.ToString(CultureInfo.InvariantCulture)}&name={Segment(search)}&use_regex=false";
        var data = await RequestAsync(HttpMethod.Get, path, default, false, cancellationToken).ConfigureAwait(false);
        if (data.ValueKind == JsonValueKind.Object && data.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var record = new RabbitMqRecord(data);
            return new(items.EnumerateArray().Select(SafeRecord).ToArray(), (int)(record.Number("page") ?? page), pageSize,
                (int)(record.Number("filtered_count") ?? record.Number("total_count") ?? items.GetArrayLength()));
        }
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() > 10000) throw new RabbitMqFailure("RabbitMqResponseTooLarge");
        var rows = data.EnumerateArray().Select(SafeRecord).Where(row => search.Length == 0 ||
            new[] { row.Name, row.String("user"), row.String("source"), row.String("destination"), row.String("exchange") }
                .Any(value => value.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray();
        int effective = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(rows.Length / (double)pageSize)));
        return new(rows.Skip((effective - 1) * pageSize).Take(pageSize).ToArray(), effective, pageSize, rows.Length, true);
    }
    /// <inheritdoc />
    public async Task<RabbitMqRecord> ReadObjectAsync(RabbitMqResource resource, string vhost, string name, CancellationToken cancellationToken = default)
    {
        string path = resource switch
        {
            RabbitMqResource.Overview => $"vhosts/{Segment(vhost)}",
            RabbitMqResource.Queues => $"queues/{Segment(vhost)}/{Segment(name)}?lengths_age=60&lengths_incr=5&msg_rates_age=60&msg_rates_incr=5",
            RabbitMqResource.Exchanges => $"exchanges/{Segment(vhost)}/{Exchange(name)}?msg_rates_age=60&msg_rates_incr=5",
            RabbitMqResource.Connections => $"connections/{Segment(name)}",
            RabbitMqResource.Channels => $"channels/{Segment(name)}",
            _ => throw Text.Exception("RabbitMqConfigurationInvalid"),
        };
        return SafeRecord(await RequestAsync(HttpMethod.Get, path, default, false, cancellationToken).ConfigureAwait(false));
    }
    private static RabbitMqRecord SafeRecord(JsonElement source)
    {
        // 用户列表的哈希和外部凭据字段永不进入视图模型、复制或 Agent 上下文。
        if (source.ValueKind != JsonValueKind.Object) throw new RabbitMqFailure("RabbitMqInvalidResponse");
        return new(JsonSerializer.SerializeToElement(source.EnumerateObject().Where(item => item.Name is not ("password" or "password_hash" or "hashing_algorithm"))
            .ToDictionary(item => item.Name, item => item.Value.Clone(), StringComparer.Ordinal)));
    }
    /// <inheritdoc />
    public async Task<IReadOnlyList<RabbitMqMessage>> GetMessagesAsync(string vhost, string queue, int count, bool remove, CancellationToken cancellationToken = default)
    {
        if (count is < 1 or > 100 || queue.Length == 0) throw Text.Exception("RabbitMqGetInvalid");
        var detail = await ReadObjectAsync(RabbitMqResource.Queues, vhost, queue, cancellationToken).ConfigureAwait(false);
        if (detail.String("type") == "stream") throw Text.Exception("RabbitMqStreamUnsupported");
        var body = JsonSerializer.SerializeToElement(new { count, ackmode = remove ? "ack_requeue_false" : "ack_requeue_true", encoding = "base64", truncate = 65536 });
        var data = await RequestAsync(HttpMethod.Post, $"queues/{Segment(vhost)}/{Segment(queue)}/get", body, true, cancellationToken).ConfigureAwait(false);
        try
        {
            return data.EnumerateArray().Select(item =>
            {
                var row = new RabbitMqRecord(item); byte[] bytes = row.String("payload_encoding") == "base64"
                    ? Convert.FromBase64String(row.String("payload")) : Encoding.UTF8.GetBytes(row.String("payload"));
                if (bytes.Length > 65536) throw new FormatException();
                return new RabbitMqMessage(bytes, (long)(row.Number("payload_bytes") ?? bytes.Length), row.String("exchange"),
                    row.String("routing_key"), row.Boolean("redelivered"), row.Value("properties").Clone());
            }).ToArray();
        }
        catch (Exception error) when (error is FormatException or InvalidOperationException)
        { throw new RabbitMqFailure("RabbitMqOutcomeUnknown", true); }
    }
}
