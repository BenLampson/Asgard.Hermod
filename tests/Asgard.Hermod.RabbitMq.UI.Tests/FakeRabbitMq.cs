namespace Asgard.Hermod.RabbitMq.UI.Tests;

internal sealed class FakeRabbitMq : IRabbitMqSession, IRabbitMqSessionFactory
{
    internal static InstanceConnectionOptions Connection => new()
    {
        TypeId = RabbitMqIds.Type,
        ProviderId = RabbitMqIds.Direct,
        Parameters = new Dictionary<string, string> { ["host"] = "mq.example.test", ["user"] = "operator" }
    };
    public RabbitMqSessionOptions Options { get; } = RabbitMqSessionOptions.Parse(Connection);
    public RabbitMqRecord Identity => Row("""{"name":"operator","tags":["administrator"]}""");
    public string Version => "4.2.0";
    public bool IsConnected { get; set; } = true;
    public bool IsAdministrator { get; set; } = true;
    public bool CanManagePolicies { get; set; } = true;
    public bool CanMonitor => IsAdministrator;
    internal bool NoVhosts { get; set; }
    internal bool NoPermissions { get; set; }
    internal int Gets { get; private set; }
    internal bool Removed { get; private set; }
    internal int Reads { get; private set; }
    internal RabbitMqFailure? Failure { get; set; }
    internal List<RabbitMqMutation> Mutations { get; } = [];
    internal TaskCompletionSource<RabbitMqPage>? PendingQueues { get; set; }
    public Task<IRabbitMqSession> OpenAsync(InstanceConnectionOptions options, CancellationToken cancellationToken = default) { IsConnected = true; return Task.FromResult<IRabbitMqSession>(this); }
    public async Task<RabbitMqPage> ReadPageAsync(RabbitMqResource resource, string vhost = "", string search = "", int page = 1, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        Reads++;
        if (resource == RabbitMqResource.Queues && PendingQueues is { } pending) return await pending.Task;
        RabbitMqRecord[] rows = resource switch
        {
            RabbitMqResource.Vhosts => NoVhosts ? [] : [Row("""{"name":"/production","description":"Production","default_queue_type":"classic"}""")],
            RabbitMqResource.OwnPermissions => NoVhosts || NoPermissions ? [] : [Row("""{"vhost":"/production","user":"operator","read":".*"}""")],
            RabbitMqResource.Queues => [Queue("orders.created"), Queue("payments.completed"), Queue("notifications.email"), Queue("inventory.sync"), Queue("orders.retry"), Queue("orders.dlq")],
            RabbitMqResource.Exchanges => [Row("""{"name":"","type":"direct","durable":true,"internal":false,"auto_delete":false}"""), Row("""{"name":"events","type":"topic","durable":true}""")],
            RabbitMqResource.Users => [Row("""{"name":"operator","tags":["administrator"]}""")],
            RabbitMqResource.Permissions => [Row("""{"user":"operator","configure":".*","read":".*","write":".*"}""")],
            RabbitMqResource.TopicPermissions => [Row("""{"user":"operator","exchange":"events","read":"^orders\\.","write":"^orders\\."}""")],
            RabbitMqResource.Policies => [Row("""{"name":"orders-retention","pattern":"^orders\\.","priority":10,"apply-to":"queues","definition":{"message-ttl":86400000}}""")],
            RabbitMqResource.Bindings => [Row("""{"source":"events","destination":"orders.created","destination_type":"queue","routing_key":"orders.created","properties_key":"orders.created","arguments":{}}""")],
            _ => [],
        };
        return new(rows, page, pageSize, resource == RabbitMqResource.Queues ? 12 : rows.Length);
    }
    public Task<RabbitMqRecord> ReadObjectAsync(RabbitMqResource resource, string vhost, string name, CancellationToken cancellationToken = default) => Task.FromResult(Queue(name));
    public Task<JsonElement> ExecuteAsync(RabbitMqMutation mutation, CancellationToken cancellationToken = default)
    { Mutations.Add(mutation); if (Failure is not null) throw Failure; return Task.FromResult(JsonSerializer.SerializeToElement(new { routed = true })); }
    public Task<IReadOnlyList<RabbitMqMessage>> GetMessagesAsync(string vhost, string queue, int count, bool remove, CancellationToken cancellationToken = default)
    {
        Gets++; Removed = remove; if (Failure is not null) throw Failure;
        return Task.FromResult<IReadOnlyList<RabbitMqMessage>>([new(Encoding.UTF8.GetBytes("{\"orderId\":\"ORD-10342\",\"status\":\"created\"}"), 96000, "events", "orders.created", false, JsonSerializer.SerializeToElement(new { content_type = "application/json", headers = new { source = "checkout" } }))]);
    }
    public ValueTask DisposeAsync() { IsConnected = false; return ValueTask.CompletedTask; }
    internal static RabbitMqRecord Row(string json) => new(JsonSerializer.Deserialize<JsonElement>(json));
    internal static RabbitMqRecord Queue(string name) => new(JsonSerializer.SerializeToElement(new
    {
        name,
        vhost = "/production",
        type = "classic",
        state = "running",
        messages_ready = 1248,
        messages_unacknowledged = 32,
        messages = 1280,
        consumers = 4,
        durable = true,
        arguments = new { },
        message_stats = new { publish_details = new { rate = 128.4, samples = Enumerable.Range(0, 13).Select(index => new { timestamp = 1700000000000L + index * 5000L, sample = index * index * 50 }) }, deliver_get_details = new { rate = 124.1, samples = Enumerable.Range(0, 13).Select(index => new { timestamp = 1700000000000L + index * 5000L, sample = index * index * 48 }) } }
    }));
}
