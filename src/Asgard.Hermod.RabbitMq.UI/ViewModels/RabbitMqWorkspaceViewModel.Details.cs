namespace Asgard.Hermod.RabbitMq.UI.ViewModels;

public sealed partial class RabbitMqWorkspaceViewModel
{
    private IReadOnlyList<RabbitMqRow> _detailRows = [];
    public string DetailTab
    {
        get; set
        {
            if (!SetProperty(ref field, value)) return;
            OnPropertyChanged(nameof(IsDetailOverview)); OnPropertyChanged(nameof(IsDetailArguments)); OnPropertyChanged(nameof(IsDetailTable));
            if (CanRead && IsDetails) _ = LoadAsync();
        }
    } = "overview";
    public IReadOnlyList<InstanceFieldChoice> DetailTabs => Resource == RabbitMqResource.Queues
        ? [new("overview", Text.Get("RabbitMqOverview")), new("consumers", Text.Get("RabbitMqConsumers")), new("bindings", Text.Get("RabbitMqBindings")), new("arguments", Text.Get("RabbitMqArgumentsPolicies"))]
        : Resource == RabbitMqResource.Exchanges ? [new("overview", Text.Get("RabbitMqOverview")), new("bindings", Text.Get("RabbitMqBindings")), new("arguments", Text.Get("RabbitMqArguments"))]
        : [new("overview", Text.Get("RabbitMqOverview")), new("arguments", Text.Get("RabbitMqDetails"))];
    public bool IsDetailOverview => DetailTab == "overview";
    public bool IsDetailArguments => DetailTab == "arguments";
    public bool IsDetailTable => DetailTab is "bindings" or "consumers";
    public IReadOnlyList<RabbitMqRow> DetailRows => _detailRows;
    public IReadOnlyList<RabbitMqColumn> DetailColumns => ColumnsFor(DetailTab == "consumers" ? RabbitMqResource.Consumers : RabbitMqResource.Bindings);
    public ObservableCollection<RabbitMqRow> Nodes { get; } = [];
    public IReadOnlyList<RabbitMqColumn> NodeColumns => RabbitMqColumn.For(RabbitMqResource.Nodes);
    public bool HasNodes => IsOverview && _session?.CanMonitor == true;
    public bool HasChart => IsOverview || Resource is RabbitMqResource.Queues or RabbitMqResource.Exchanges;
    public RabbitMqRecord? DetailChart => _detail;
    public IReadOnlyList<RabbitMqMetric> Metrics => IsOverview ? [Metric("messages_ready", "RabbitMqReady"), Metric("messages_unacknowledged", "RabbitMqUnacked"), Metric("message_stats.publish_details.rate", "RabbitMqIncoming"), Metric("message_stats.deliver_get_details.rate", "RabbitMqOutgoing")] : Resource is RabbitMqResource.Connections or RabbitMqResource.Channels
        ? [Metric("channels", "RabbitMqChannels"), Metric("consumer_count", "RabbitMqConsumers"), Metric("messages_unacknowledged", "RabbitMqUnacked"), Metric("prefetch_count", "RabbitMqPrefetch")]
        : [Metric("messages_ready", "RabbitMqReady"), Metric("messages_unacknowledged", "RabbitMqUnacked"), Metric("consumers", "RabbitMqConsumers"), Metric("messages", "RabbitMqTotal")];
    public string DetailJson
    {
        get
        {
            if (_detail is null) return "";
            if (Resource is RabbitMqResource.Connections or RabbitMqResource.Channels) return Pretty(_detail.Data);
            return JsonSerializer.Serialize(new
            {
                arguments = _detail.Value("arguments").ValueKind == JsonValueKind.Undefined ? null : (object)_detail.Value("arguments"),
                policy = _detail.String("policy"),
                effective_policy_definition = _detail.Value("effective_policy_definition").ValueKind == JsonValueKind.Undefined ? null : (object)_detail.Value("effective_policy_definition")
            }, new JsonSerializerOptions { WriteIndented = true });
        }
    }
    private RabbitMqMetric Metric(string key, string label) => new(Text.Get(label), _detail is null ? Text.Get("RabbitMqUnavailable") : new RabbitMqRow(_detail)[key], Text.Get(IsOverview ? "RabbitMqCurrentVhost" : label + "Hint"));
    private async Task LoadDetailRowsAsync(CancellationToken token)
    {
        if (_detail is null) return;
        int revision = _revision;
        if (DetailTab == "consumers")
        {
            var data = _detail.Value("consumer_details");
            _detailRows = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Select(item => new RabbitMqRow(new(item.Clone()))).ToArray() : [];
        }
        else if (DetailTab == "bindings")
        {
            var bindings = new List<RabbitMqRow>();
            for (int page = 1; page <= 100; page++)
            {
                var data = await _session!.ReadPageAsync(RabbitMqResource.Bindings, SelectedVhost!, page: page, pageSize: 100, cancellationToken: token);
                bindings.AddRange(data.Items.Where(item => Resource == RabbitMqResource.Queues
                    ? item.String("destination_type") == "queue" && item.String("destination") == _detailName
                    : item.String("source") == _detailName || item.String("destination_type") == "exchange" && item.String("destination") == _detailName).Select(item => new RabbitMqRow(item)));
                if (page >= data.PageCount) break;
            }
            if (revision == _revision) _detailRows = bindings;
        }
    }
    private static string Pretty(JsonElement value) => value.ValueKind == JsonValueKind.Undefined ? "" : JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });
}
